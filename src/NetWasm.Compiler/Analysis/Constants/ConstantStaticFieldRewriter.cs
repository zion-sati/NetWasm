using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// A static readonly field is a compile-time constant where its owner's static
// constructor stores a constant in it before running anything else and never
// stores it again: nothing may write it once the type is initialized, and no
// code can read it before that store. Its load becomes the constant, which
// later folding sees, as .NET's JIT reads such a field. A primitive or enum
// field reads the constant as its width stores it, and a reference field left
// null reads null.
// Where the constructor does nothing else, the owner's fields hold their
// constants, or zero, from the start and running it has no effect, so the load
// needs no initialization check, as BitConverter.IsLittleEndian in
// CharUnicodeInfo's table lookups. Where it does more, as a later initializer
// that calls a method, the load keeps the owner's initialization, the field's
// address dropped as BeforeFieldInitEntryInitializer writes one, so the
// constructor still runs, and fails, where .NET's would.
internal sealed class ConstantStaticFieldRewriter(
    IFieldRepository fields,
    ITypeIdentityResolver typeIdentities,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodBodyReader methodBodies) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<EntityKey, Initialization> _initializers = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<CilInstruction>[]? replacements = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            // A volatile load keeps its memory semantics.
            if (instruction.Operation != CilOperation.LoadStaticField ||
                index > 0 && body.Instructions[index - 1].Operation == CilOperation.Volatile ||
                Constant(fields.GetField(CilOperands.FieldKey(instruction.Operand)), out var initializes) is not { } constant)
            {
                continue;
            }
            replacements ??= [.. body.Instructions.Select(original => new List<CilInstruction> { original })];
            replacements[index] = initializes
                ? [instruction with { Operation = CilOperation.LoadStaticFieldAddress }, Load(CilOperation.Pop, new CilOperand.None()), constant]
                : [constant];
        }
        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    // The load of the constant the field holds, as its type reads back what was
    // stored, and whether reading it must still initialize its owner, or null.
    private CilInstruction? Constant(FieldDefinitionModel field, out bool initializes)
    {
        initializes = false;
        if (!field.IsStatic || !field.IsInitOnly || field.IsLiteral || !field.InitialData.IsDefaultOrEmpty)
        {
            return null;
        }
        var reference = field.SignatureType is
        {
            IsValueType: false, Shape: not (CliTypeShape.GenericTypeParameter or
            CliTypeShape.GenericMethodParameter or CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or
            CliTypeShape.FunctionPointer or CliTypeShape.Modified)
        };
        var type = reference ? field.SignatureType : Primitive(field.SignatureType);
        if (type is null) return null;
        var owner = typeDefinitions.ResolveTypeIdentity(typeIdentities.GetTypeIdentity(field.DeclaringType));
        var initialization = _initializers.GetOrAdd(owner.Key, _ => Initializer(owner));
        // A field the constructor does not store first holds zero only where it does nothing else.
        if (!initialization.Constants.TryGetValue(field.Key, out var value))
        {
            if (!initialization.Complete) return null;
            value = new(reference ? CliValueKind.ManagedReference : type.StackKind is CliValueKind.F4 ? CliValueKind.F8 : type.StackKind, 0, 0);
        }
        initializes = !initialization.Complete;
        return (reference, type.CanonicalName, value.Kind) switch
        {
            (true, _, CliValueKind.ManagedReference) => Load(CilOperation.LoadNull, new CilOperand.None()),
            (false, "primitive:bool" or "primitive:u1", CliValueKind.I4) => Int32((byte)value.Integer),
            (false, "primitive:i1", CliValueKind.I4) => Int32((sbyte)value.Integer),
            (false, "primitive:char" or "primitive:u2", CliValueKind.I4) => Int32((ushort)value.Integer),
            (false, "primitive:i2", CliValueKind.I4) => Int32((short)value.Integer),
            (false, "primitive:i4" or "primitive:u4", CliValueKind.I4) => Int32((int)value.Integer),
            (false, "primitive:i8" or "primitive:u8", CliValueKind.I8) =>
                Load(CilOperation.LoadInt64, new CilOperand.ConstantI8(value.Integer)),
            (false, "primitive:f4", CliValueKind.F8) => Load(CilOperation.LoadFloat32, new CilOperand.ConstantF4((float)value.Real)),
            (false, "primitive:f8", CliValueKind.F8) => Load(CilOperation.LoadFloat64, new CilOperand.ConstantF8(value.Real)),
            _ => null,
        };
    }

    // The primitive type a field of this type holds: its own, or an enum's
    // underlying type.
    private CliTypeIdentity? Primitive(CliTypeIdentity type)
    {
        if (type.Shape == CliTypeShape.Primitive) return type;
        if (type.Shape != CliTypeShape.Named || !type.IsValueType) return null;
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        return definition.IsEnum && definition.EnumUnderlyingType.Shape == CliTypeShape.Primitive
            ? definition.EnumUnderlyingType
            : null;
    }

    // The constants the static constructor stores in the owner's static fields
    // before anything else runs, less any field it stores or addresses again,
    // and whether that is all it does. No constructor stores nothing.
    private Initialization Initializer(TypeDefinitionModel owner)
    {
        var constructor = owner.Methods.Select(methods.GetMethod).SingleOrDefault(method => method.Name == ".cctor");
        if (constructor is null) return new([], Complete: true);
        var body = methodBodies.ReadMethodBody(constructor);
        var operations = body.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
        var stored = new Dictionary<EntityKey, Stored>();
        var pending = (Stored?)null;
        var leading = 0;
        while (leading < operations.Length && Leading(operations[leading])) leading++;
        // A constant waiting for its store belongs to what follows.
        if (pending is not null) leading--;
        foreach (var operation in operations[leading..])
        {
            if (operation.Operation is CilOperation.StoreStaticField or CilOperation.LoadStaticFieldAddress)
            {
                stored.Remove(CilOperands.FieldKey(operation.Operand));
            }
        }
        return new(stored, operations.Length != 0 && leading == operations.Length - 1 &&
            operations[^1].Operation == CilOperation.Return && body.ExceptionRegions.IsEmpty);

        // A constant load, or its store to the owner's own static field: neither runs anything else.
        bool Leading(CilInstruction operation)
        {
            switch (operation)
            {
                case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 constant } when pending is null:
                    pending = new(CliValueKind.I4, constant.Value, 0);
                    return true;
                case { Operation: CilOperation.LoadInt64, Operand: CilOperand.ConstantI8 constant } when pending is null:
                    pending = new(CliValueKind.I8, constant.Value, 0);
                    return true;
                // Roslyn writes a small long as an int and a widening.
                case { Operation: CilOperation.ConvertInt64 } when pending is { Kind: CliValueKind.I4 } value:
                    pending = value with { Kind = CliValueKind.I8 };
                    return true;
                case { Operation: CilOperation.ConvertInt64Unsigned } when pending is { Kind: CliValueKind.I4 } value:
                    pending = new(CliValueKind.I8, (uint)value.Integer, 0);
                    return true;
                // The evaluation stack holds every float as F, whatever its width.
                case { Operation: CilOperation.LoadFloat32, Operand: CilOperand.ConstantF4 constant } when pending is null:
                    pending = new(CliValueKind.F8, 0, constant.Value);
                    return true;
                case { Operation: CilOperation.LoadFloat64, Operand: CilOperand.ConstantF8 constant } when pending is null:
                    pending = new(CliValueKind.F8, 0, constant.Value);
                    return true;
                case { Operation: CilOperation.LoadNull } when pending is null:
                    pending = new(CliValueKind.ManagedReference, 0, 0);
                    return true;
                case { Operation: CilOperation.StoreStaticField } when pending is { } value &&
                    fields.GetField(CilOperands.FieldKey(operation.Operand)) is var target &&
                    target.DeclaringType == owner.Key:
                    stored[target.Key] = value;
                    pending = null;
                    return true;
                default:
                    return false;
            }
        }
    }

    private static CilInstruction Int32(int value) => Load(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));

    private static CilInstruction Load(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);

    // A constant as the evaluation stack holds it.
    private readonly record struct Stored(CliValueKind Kind, long Integer, double Real);

    private sealed record Initialization(Dictionary<EntityKey, Stored> Constants, bool Complete);
}
