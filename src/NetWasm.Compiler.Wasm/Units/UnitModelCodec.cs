using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Writes the compiler's models structurally, so a later compile can rebuild them through
/// the same factories that built them: a type identity by its shape and parts (a named type
/// by assembly, namespace and name), a method or field instance by its definition's key, its
/// declaring type and its arguments. An identity written twice is written once and referred
/// to after, so equal models encode alike however many instances the compile made of them.
/// </summary>
internal sealed class UnitModelWriter
{
    private readonly ArrayBufferWriter<byte> _bytes = new(256);
    private readonly Dictionary<CliTypeIdentity, int> _types = new(UnitTypeComparer.Instance);

    internal byte[] ToArray() => _bytes.WrittenSpan.ToArray();

    internal void Int(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_bytes.GetSpan(4), value);
        _bytes.Advance(4);
    }

    internal void Long(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_bytes.GetSpan(8), value);
        _bytes.Advance(8);
    }

    internal void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    internal void Byte(byte value)
    {
        _bytes.GetSpan(1)[0] = value;
        _bytes.Advance(1);
    }

    internal void String(string? value)
    {
        if (value is null)
        {
            Int(-1);
            return;
        }
        Int(value.Length);
        _bytes.Write(MemoryMarshal.AsBytes(value.AsSpan()));
    }

    internal void Bytes(ReadOnlySpan<byte> values)
    {
        Int(values.Length);
        _bytes.Write(values);
    }

    internal void Ints(ImmutableArray<int> values)
    {
        Int(values.IsDefault ? -1 : values.Length);
        if (values.IsDefault) return;
        foreach (var value in values) Int(value);
    }

    internal void Entity(EntityKey key)
    {
        String(key.Assembly.Name);
        Int(key.MetadataToken);
    }

    internal void Type(CliTypeIdentity? type)
    {
        if (type is null)
        {
            Int(-1);
            return;
        }
        if (_types.TryGetValue(type, out var known))
        {
            Int(known);
            return;
        }
        Int(-2);
        Byte((byte)type.Shape);
        switch (type.Shape)
        {
            case CliTypeShape.Primitive:
                String(type.CanonicalName["primitive:".Length..]);
                break;
            case CliTypeShape.Named:
                String(type.Assembly!.Value.Name);
                String(type.FullName);
                break;
            case CliTypeShape.SzArray:
            case CliTypeShape.ManagedByReference:
            case CliTypeShape.UnmanagedPointer:
                Type(type.ElementType);
                break;
            case CliTypeShape.Array:
                Type(type.ElementType);
                Int(type.ArrayRank);
                break;
            case CliTypeShape.GenericInstantiation:
                Type(type.ElementType);
                Types(type.TypeArguments);
                break;
            case CliTypeShape.GenericTypeParameter:
            case CliTypeShape.GenericMethodParameter:
                // A scoped parameter names its owner in its canonical name.
                String(type.CanonicalName.StartsWith("generic-parameter:", StringComparison.Ordinal)
                    ? type.CanonicalName["generic-parameter:".Length..type.CanonicalName.LastIndexOf(':')]
                    : null);
                Int(type.GenericParameterIndex);
                break;
            case CliTypeShape.FunctionPointer:
                var pointer = type.FunctionPointerSignature!;
                Byte(pointer.Header);
                Int(pointer.GenericArity);
                Int(pointer.RequiredParameterCount);
                Signature(pointer.Signature);
                break;
            case CliTypeShape.Modified:
                Type(type.ElementType);
                Type(type.CustomModifier);
                Bool(type.IsRequiredModifier);
                break;
        }
        Bool(type.IsValueType);
        Byte((byte)type.StackKind);
        Type(type.StackStorageType);
        _types[type] = _types.Count;
    }

    internal void Types(ImmutableArray<CliTypeIdentity> types)
    {
        Int(types.IsDefault ? 0 : types.Length);
        if (types.IsDefault) return;
        foreach (var type in types) Type(type);
    }

    internal void Signature(MethodSignatureModel signature)
    {
        Byte((byte)signature.ReturnType);
        Int(signature.ParameterTypes.IsDefault ? 0 : signature.ParameterTypes.Length);
        if (!signature.ParameterTypes.IsDefault)
        {
            foreach (var kind in signature.ParameterTypes) Byte((byte)kind);
        }
        Type(signature.ReturnSignatureType);
        Types(signature.ParameterSignatureTypes);
    }

    internal void Method(MethodInstanceModel method)
    {
        Entity(method.Definition.Key);
        Type(method.DeclaringType);
        Types(method.MethodArguments);
        Signature(method.Signature);
    }

    internal void Field(FieldInstanceModel field)
    {
        Entity(field.Definition.Key);
        Type(field.DeclaringType);
        Type(field.FieldType);
    }
}

/// <summary>
/// Type identities that rebuild alike: equal canonical names (which spell out the shape and
/// parts) with the same stack kind, value kind and stack storage type. Identity equality
/// alone compares canonical names, which a stack kind does not change.
/// </summary>
internal sealed class UnitTypeComparer : IEqualityComparer<CliTypeIdentity>
{
    public static UnitTypeComparer Instance { get; } = new();

    public bool Equals(CliTypeIdentity? x, CliTypeIdentity? y) =>
        ReferenceEquals(x, y) ||
        (x is not null && y is not null && x.Equals(y) && x.StackKind == y.StackKind &&
         x.IsValueType == y.IsValueType && Equals(x.StackStorageType, y.StackStorageType));

    public int GetHashCode(CliTypeIdentity obj) => obj.GetHashCode();
}

/// <summary>Reads what <see cref="UnitModelWriter"/> wrote, resolving definitions by key.</summary>
internal sealed class UnitModelReader(ReadOnlyMemory<byte> bytes, IMethodRepository methods, IFieldRepository fields)
{
    private readonly List<CliTypeIdentity> _types = [];
    private int _at;

    internal bool AtEnd => _at == bytes.Length;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _at + count > bytes.Length) throw new InvalidDataException("A unit value is truncated.");
        var span = bytes.Span.Slice(_at, count);
        _at += count;
        return span;
    }

    internal int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    internal long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    internal byte Byte() => Take(1)[0];

    internal bool Bool() => Byte() != 0;

    internal string? String()
    {
        var length = Int();
        return length < 0 ? null : new string(MemoryMarshal.Cast<byte, char>(Take(checked(length * 2))));
    }

    internal ReadOnlySpan<byte> Bytes() => Take(Int());

    internal ImmutableArray<int> Ints()
    {
        var count = Int();
        if (count < 0) return default;
        var values = ImmutableArray.CreateBuilder<int>(count);
        for (var index = 0; index < count; index++) values.Add(Int());
        return values.MoveToImmutable();
    }

    internal EntityKey Entity()
    {
        var assembly = String() ?? throw new InvalidDataException("A unit value names no assembly.");
        return new(new AssemblyIdentity(assembly), Int());
    }

    internal CliTypeIdentity? Type()
    {
        var tag = Int();
        if (tag == -1) return null;
        if (tag >= 0) return tag < _types.Count ? _types[tag] : throw new InvalidDataException("A unit value refers to an unknown type.");
        if (tag != -2) throw new InvalidDataException("A unit value's type is corrupt.");
        var shape = (CliTypeShape)Byte();
        CliTypeIdentity type;
        switch (shape)
        {
            case CliTypeShape.Primitive:
                {
                    var name = String()!;
                    var isValueType = PeekValueType();
                    type = CliTypeIdentity.Primitive(name, PeekStackKind(), isValueType);
                    break;
                }
            case CliTypeShape.Named:
                {
                    // A named type's assembly is always written, its name empty when it has none.
                    var assembly = String() ?? throw new InvalidDataException("A named unit type has no assembly.");
                    var fullName = String() ?? throw new InvalidDataException("A named unit type has no name.");
                    // Namespaces never contain '+'; a nested type's name begins after its namespace.
                    var nested = fullName.IndexOf('+');
                    var dot = (nested < 0 ? fullName : fullName[..nested]).LastIndexOf('.');
                    type = CliTypeIdentity.Named(new AssemblyIdentity(assembly),
                        dot < 0 ? "" : fullName[..dot], dot < 0 ? fullName : fullName[(dot + 1)..], PeekValueType());
                    break;
                }
            case CliTypeShape.SzArray:
                type = CliTypeIdentity.SzArray(Required());
                break;
            case CliTypeShape.ManagedByReference:
                type = CliTypeIdentity.ManagedByReference(Required());
                break;
            case CliTypeShape.UnmanagedPointer:
                type = CliTypeIdentity.UnmanagedPointer(Required());
                break;
            case CliTypeShape.Array:
                {
                    var element = Required();
                    type = CliTypeIdentity.Array(element, Int());
                    break;
                }
            case CliTypeShape.GenericInstantiation:
                {
                    var generic = Required();
                    type = CliTypeIdentity.GenericInstantiation(generic, Types());
                    break;
                }
            case CliTypeShape.GenericTypeParameter:
            case CliTypeShape.GenericMethodParameter:
                {
                    var owner = String();
                    var index = Int();
                    var method = shape == CliTypeShape.GenericMethodParameter;
                    type = owner is null
                        ? CliTypeIdentity.GenericParameter(method, index)
                        : CliTypeIdentity.ScopedGenericParameter(owner, method, index);
                    break;
                }
            case CliTypeShape.FunctionPointer:
                {
                    var header = Byte();
                    var arity = Int();
                    var required = Int();
                    type = CliTypeIdentity.FunctionPointer(new(header, arity, required, Signature()));
                    break;
                }
            case CliTypeShape.Modified:
                {
                    var element = Required();
                    var modifier = Required();
                    type = CliTypeIdentity.Modified(element, modifier, Bool());
                    break;
                }
            default:
                throw new InvalidDataException($"A unit type has an unknown shape {(int)shape}.");
        }
        var isValue = Bool();
        var kind = (CliValueKind)Byte();
        var storage = Type();
        if (type.IsValueType != isValue)
            throw new InvalidDataException($"A unit type {type.CanonicalName} no longer has its value kind.");
        if (storage is not null && !Equals(type.StackStorageType, storage)) type = type.WithStackStorageType(storage);
        if (type.StackKind != kind) type = type.WithStackKind(kind);
        _types.Add(type);
        return type;
    }

    // Every type ends with its value kind and stack kind. A primitive's and a named type's
    // factories take them, and nothing of these shapes comes between their name and them.
    private bool PeekValueType() =>
        _at < bytes.Length ? bytes.Span[_at] != 0 : throw new InvalidDataException("A unit value is truncated.");

    private CliValueKind PeekStackKind() =>
        _at + 1 < bytes.Length ? (CliValueKind)bytes.Span[_at + 1] : throw new InvalidDataException("A unit value is truncated.");

    private CliTypeIdentity Required() => Type() ?? throw new InvalidDataException("A unit type lacks a part.");

    internal ImmutableArray<CliTypeIdentity> Types()
    {
        var count = Int();
        if (count == 0) return [];
        var types = ImmutableArray.CreateBuilder<CliTypeIdentity>(count);
        for (var index = 0; index < count; index++) types.Add(Required());
        return types.MoveToImmutable();
    }

    internal MethodSignatureModel Signature()
    {
        var returnKind = (CliValueKind)Byte();
        var count = Int();
        ImmutableArray<CliValueKind> parameterKinds = [];
        if (count != 0)
        {
            var builder = ImmutableArray.CreateBuilder<CliValueKind>(count);
            for (var index = 0; index < count; index++) builder.Add((CliValueKind)Byte());
            parameterKinds = builder.MoveToImmutable();
        }
        var returnType = Required();
        var parameters = Types();
        return new MethodSignatureModel(returnType, parameters)
        {
            ReturnType = returnKind,
            ParameterTypes = parameterKinds,
        };
    }

    internal MethodInstanceModel Method()
    {
        var definition = methods.GetMethod(Entity());
        var declaring = Required();
        var arguments = Types();
        return new MethodInstanceModel(definition, declaring, arguments, Signature());
    }

    internal FieldInstanceModel Field()
    {
        var definition = fields.GetField(Entity());
        var declaring = Required();
        return new FieldInstanceModel(definition, declaring, Required());
    }
}
