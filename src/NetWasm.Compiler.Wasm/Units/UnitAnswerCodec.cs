using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Encodes recorded answers by query kind, so a later compile can answer a query again
/// without the compile that answered it (the early cutoff's replay). A kind it cannot encode
/// keeps no value, and a compile that would need that answer cannot replay.
/// </summary>
internal static class UnitAnswerCodec
{
    private const byte Format = 2;

    // The answer's encoding, or null for a kind that keeps no value. A site's caller is the
    // key's own ("caller@offset"), so it is written only when it is not.
    public static byte[]? Encode(UnitQueryKind kind, string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var caller = Caller(key);
        if (value is UnitEncodedValue encoded) return encoded.Bytes;
        if (value is null) return null;
        var writer = new UnitModelWriter();
        writer.Byte(Format);
        switch (kind)
        {
            case UnitQueryKind.StaticDataEnd:
            case UnitQueryKind.TypeFactsTable:
            case UnitQueryKind.TypeObjectTable:
            case UnitQueryKind.MethodDescriptorAddress:
            case UnitQueryKind.FieldDescriptorAddress:
            case UnitQueryKind.MemberDescriptorOffsets:
            case UnitQueryKind.ExceptionObject:
            case UnitQueryKind.ReferenceArrayTypeId:
            case UnitQueryKind.StringTypeId:
            case UnitQueryKind.TypeTypeId:
                writer.Int((int)value);
                break;
            case UnitQueryKind.ObjectLayout:
            case UnitQueryKind.ObjectLayoutOfDefinition:
                Object(writer, (ObjectLayout)value);
                break;
            case UnitQueryKind.ObjectLayoutLookup:
                {
                    var lookup = (UnitLookup<ObjectLayout>)value;
                    writer.Bool(lookup.Found);
                    if (lookup.Found) Object(writer, lookup.Value);
                    break;
                }
            case UnitQueryKind.ValueLayout:
                {
                    var layout = (ValueLayout)value;
                    writer.Type(layout.Type);
                    writer.Int(layout.Size);
                    writer.Int(layout.Alignment);
                    writer.Ints(layout.ReferenceOffsets);
                    writer.Ints(layout.ByReferenceOffsets);
                    break;
                }
            case UnitQueryKind.InstanceField:
            case UnitQueryKind.InstanceFieldOfDefinition:
                {
                    var layout = (FieldLayout)value;
                    writer.Int(layout.Offset);
                    writer.Int(layout.Size);
                    writer.Type(layout.Type);
                    break;
                }
            case UnitQueryKind.StaticField:
            case UnitQueryKind.StaticFieldOfDefinition:
                writer.Int(((StaticFieldLayout)value).Address);
                break;
            case UnitQueryKind.StringLayout:
                {
                    var layout = (StringLayout)value;
                    writer.Int(layout.Address);
                    writer.Int(layout.Length);
                    writer.Int(layout.DataOffset);
                    break;
                }
            case UnitQueryKind.StaticRootAddresses:
                writer.Ints((ImmutableArray<int>)value);
                break;
            case UnitQueryKind.DataSegments:
                {
                    var segments = (ImmutableArray<DataSegment>)value;
                    writer.Int(segments.Length);
                    foreach (var segment in segments)
                    {
                        writer.Int(segment.Address);
                        writer.Int(segment.Data.Length);
                        foreach (var data in segment.Data) writer.Byte(data);
                    }
                    break;
                }
            case UnitQueryKind.TypeObjectAddress:
            case UnitQueryKind.FunctionAddress:
            case UnitQueryKind.StackTraceDirectMethodId:
            case UnitQueryKind.StackTraceConstructedMethodId:
                {
                    var lookup = (UnitLookup<int>)value;
                    writer.Bool(lookup.Found);
                    writer.Int(lookup.Found ? lookup.Value : 0);
                    break;
                }
            case UnitQueryKind.Finalizable:
            case UnitQueryKind.AllocatingMethod:
            case UnitQueryKind.AllocatingConstructedMethod:
                writer.Bool((bool)value);
                break;
            case UnitQueryKind.StackTraceLocationSymbols:
                {
                    var lookup = (UnitLookup<ImmutableArray<StackTraceLocationSymbol>>)value;
                    writer.Bool(lookup.Found);
                    if (!lookup.Found) break;
                    writer.Int(lookup.Value.Length);
                    foreach (var symbol in lookup.Value)
                    {
                        writer.Int(symbol.Offset);
                        writer.Int(symbol.SymbolId);
                    }
                    break;
                }
            case UnitQueryKind.StaticInitializerGuard:
                {
                    var lookup = (UnitLookup<StaticInitializerGuard>)value;
                    writer.Bool(lookup.Found);
                    if (!lookup.Found) break;
                    var guard = lookup.Value!;
                    writer.Int(guard.Address);
                    writer.Bool(guard.Direct is not null);
                    if (guard.Direct is { } direct) writer.Entity(direct);
                    writer.String(guard.Constructed);
                    writer.Int(guard.FunctionIndex.IsPresent ? guard.FunctionIndex.Value : -1);
                    break;
                }
            case UnitQueryKind.ExceptionMetadata:
                {
                    var lookup = (UnitLookup<ExceptionGroupMetadata>)value;
                    writer.Bool(lookup.Found);
                    if (!lookup.Found) break;
                    writer.Int(lookup.Value.Address);
                    writer.Int(lookup.Value.ClauseCount);
                    writer.Bool(lookup.Value.HasFilters);
                    break;
                }
            case UnitQueryKind.RootMap:
                {
                    var lookup = (UnitLookup<MethodRootMap>)value;
                    writer.Bool(lookup.Found);
                    if (lookup.Found) RootMap(writer, lookup.Value!);
                    break;
                }
            case UnitQueryKind.ManagedCallSite:
                Lookup(writer, (UnitLookup<ManagedCallSite>)value, (writer, site) =>
                {
                    Text(writer, site.Key.Caller.CanonicalName, caller);
                    writer.Int(site.Key.InstructionOffset);
                    writer.Byte((byte)site.Operation);
                    writer.Method(site.Target);
                    writer.Type(site.ConstrainedType);
                    Text(writer, site.TargetIdentity.CanonicalName, site.Target.CanonicalName);
                });
                break;
            case UnitQueryKind.DispatchCallSite:
                Lookup(writer, (UnitLookup<DispatchCallSiteModel>)value, (writer, site) =>
                {
                    Text(writer, site.Caller, caller);
                    writer.Int(site.IlOffset);
                    writer.Method(site.Declaration);
                    writer.Int(site.Targets.Length);
                    foreach (var target in site.Targets)
                    {
                        writer.Type(target.ReceiverType);
                        writer.Method(target.Method);
                    }
                });
                break;
            case UnitQueryKind.TypeTestSite:
                Lookup(writer, (UnitLookup<TypeTestSiteModel>)value, (writer, site) =>
                {
                    Text(writer, site.Caller, caller);
                    writer.Int(site.IlOffset);
                    writer.Type(site.TargetType);
                    writer.Types(site.MatchingTypes);
                });
                break;
            case UnitQueryKind.ByRefEffects:
                Lookup(writer, (UnitLookup<ManagedByRefMethodEffects>)value, static (writer, effects) =>
                {
                    Set(writer, effects.WrittenArguments);
                    Set(writer, effects.EscapedArguments);
                    Set(writer, effects.ReturnedArgumentAliases);
                });
                break;
            case UnitQueryKind.TypeDescriptors:
                Array(writer, (ImmutableArray<TypeDescriptorLayout>)value, static (writer, descriptor) =>
                {
                    writer.Entity(descriptor.Type);
                    Descriptor(writer, descriptor.TypeId, descriptor.BaseTypeId, descriptor.ObjectSize, descriptor.BitmapAddress,
                        descriptor.BitmapBitCount, descriptor.Finalizer, descriptor.AssignableTypeIdsAddress,
                        descriptor.AssignableTypeIdCount, descriptor.IsInterface);
                });
                break;
            case UnitQueryKind.ConstructedTypeDescriptors:
                Array(writer, (ImmutableArray<ConstructedTypeDescriptorLayout>)value, static (writer, descriptor) =>
                {
                    writer.Type(descriptor.Type);
                    Descriptor(writer, descriptor.TypeId, descriptor.BaseTypeId, descriptor.ObjectSize, descriptor.BitmapAddress,
                        descriptor.BitmapBitCount, descriptor.Finalizer, descriptor.AssignableTypeIdsAddress,
                        descriptor.AssignableTypeIdCount, descriptor.IsInterface);
                });
                break;
            case UnitQueryKind.ValueTypeDescriptors:
                Array(writer, (ImmutableArray<ValueTypeDescriptorLayout>)value, static (writer, descriptor) =>
                {
                    writer.Type(descriptor.Type);
                    writer.Int(descriptor.TypeId);
                    writer.Int(descriptor.Size);
                    writer.Int(descriptor.BoxedPayloadOffset);
                    writer.Int(descriptor.BitmapAddress);
                    writer.Int(descriptor.BitmapBitCount);
                });
                break;
            case UnitQueryKind.MetadataTypeDescriptors:
                Array(writer, (ImmutableArray<MetadataTypeDescriptorLayout>)value, static (writer, descriptor) =>
                {
                    writer.Type(descriptor.Type);
                    writer.Int(descriptor.TypeId);
                });
                break;
            case UnitQueryKind.EnumMetadata:
                Array(writer, (ImmutableArray<EnumMetadataLayout>)value, static (writer, layout) =>
                {
                    writer.Entity(layout.Type);
                    writer.Type(layout.EnumType);
                    writer.Int(layout.TypeId);
                    writer.Int(layout.Address);
                    writer.Type(layout.UnderlyingType);
                    writer.Bool(layout.IsFlags);
                    writer.Bool(layout.IsOpenDefinition);
                    writer.Int(layout.Members.Length);
                    foreach (var member in layout.Members)
                    {
                        writer.String(member.Name);
                        writer.Long((long)member.RawValue);
                        writer.Int(member.NameLayout.Address);
                        writer.Int(member.NameLayout.Length);
                        writer.Int(member.NameLayout.DataOffset);
                    }
                });
                break;
            case UnitQueryKind.CallableMethods:
                {
                    var methods = (IReadOnlyDictionary<string, MethodInstanceModel>)value;
                    writer.Int(methods.Count);
                    foreach (var (name, method) in methods)
                    {
                        writer.String(name);
                        writer.Method(method);
                    }
                    break;
                }
            case UnitQueryKind.DelegateTypes:
                {
                    var types = (IReadOnlyList<CliTypeIdentity>)value;
                    writer.Int(types.Count);
                    foreach (var type in types) writer.Type(type);
                    break;
                }
            default:
                return null;
        }
        return writer.ToArray();
    }

    // An answer as Encode wrote it, as the provider returned it.
    public static object Decode(UnitQueryKind kind, string key, ReadOnlySpan<byte> bytes, IMethodRepository methods,
        IFieldRepository fields)
    {
        ArgumentNullException.ThrowIfNull(key);
        var caller = Caller(key);
        var reader = new UnitModelReader(bytes.ToArray(), methods, fields);
        if (reader.Byte() != Format) throw new InvalidDataException("A unit answer is of another format.");
        object value;
        switch (kind)
        {
            case UnitQueryKind.StaticDataEnd:
            case UnitQueryKind.TypeFactsTable:
            case UnitQueryKind.TypeObjectTable:
            case UnitQueryKind.MethodDescriptorAddress:
            case UnitQueryKind.FieldDescriptorAddress:
            case UnitQueryKind.MemberDescriptorOffsets:
            case UnitQueryKind.ExceptionObject:
            case UnitQueryKind.ReferenceArrayTypeId:
            case UnitQueryKind.StringTypeId:
            case UnitQueryKind.TypeTypeId:
                value = reader.Int();
                break;
            case UnitQueryKind.ObjectLayout:
            case UnitQueryKind.ObjectLayoutOfDefinition:
                value = Object(reader);
                break;
            case UnitQueryKind.ObjectLayoutLookup:
                value = reader.Bool() ? new UnitLookup<ObjectLayout>(true, Object(reader)) : new UnitLookup<ObjectLayout>(false, default);
                break;
            case UnitQueryKind.ValueLayout:
                {
                    var type = reader.Type()!;
                    var size = reader.Int();
                    var alignment = reader.Int();
                    var references = reader.Ints();
                    value = new ValueLayout(type, size, alignment, references) { ByReferenceOffsets = reader.Ints() };
                    break;
                }
            case UnitQueryKind.InstanceField:
            case UnitQueryKind.InstanceFieldOfDefinition:
                {
                    var offset = reader.Int();
                    var size = reader.Int();
                    value = new FieldLayout(offset) { Size = size, Type = reader.Type() };
                    break;
                }
            case UnitQueryKind.StaticField:
            case UnitQueryKind.StaticFieldOfDefinition:
                value = new StaticFieldLayout(reader.Int());
                break;
            case UnitQueryKind.StringLayout:
                value = new StringLayout(reader.Int(), reader.Int(), reader.Int());
                break;
            case UnitQueryKind.StaticRootAddresses:
                value = reader.Ints();
                break;
            case UnitQueryKind.DataSegments:
                {
                    var count = reader.Int();
                    var segments = ImmutableArray.CreateBuilder<DataSegment>(count);
                    for (var index = 0; index < count; index++)
                    {
                        var address = reader.Int();
                        var length = reader.Int();
                        var data = new byte[length];
                        for (var at = 0; at < length; at++) data[at] = reader.Byte();
                        segments.Add(new DataSegment(address, [.. data]));
                    }
                    value = segments.MoveToImmutable();
                    break;
                }
            case UnitQueryKind.TypeObjectAddress:
            case UnitQueryKind.FunctionAddress:
            case UnitQueryKind.StackTraceDirectMethodId:
            case UnitQueryKind.StackTraceConstructedMethodId:
                {
                    var found = reader.Bool();
                    value = new UnitLookup<int>(found, reader.Int());
                    break;
                }
            case UnitQueryKind.Finalizable:
            case UnitQueryKind.AllocatingMethod:
            case UnitQueryKind.AllocatingConstructedMethod:
                value = reader.Bool();
                break;
            case UnitQueryKind.StackTraceLocationSymbols:
                {
                    if (!reader.Bool())
                    {
                        value = new UnitLookup<ImmutableArray<StackTraceLocationSymbol>>(false, default);
                        break;
                    }
                    var count = reader.Int();
                    var symbols = ImmutableArray.CreateBuilder<StackTraceLocationSymbol>(count);
                    for (var index = 0; index < count; index++) symbols.Add(new(reader.Int(), reader.Int()));
                    value = new UnitLookup<ImmutableArray<StackTraceLocationSymbol>>(true, symbols.MoveToImmutable());
                    break;
                }
            case UnitQueryKind.StaticInitializerGuard:
                {
                    if (!reader.Bool())
                    {
                        value = new UnitLookup<StaticInitializerGuard>(false, null);
                        break;
                    }
                    var address = reader.Int();
                    EntityKey? direct = reader.Bool() ? reader.Entity() : null;
                    var constructed = reader.String();
                    var function = reader.Int();
                    value = new UnitLookup<StaticInitializerGuard>(true, new(address, direct, constructed)
                    {
                        FunctionIndex = function >= 0 ? OptionalFunctionIndex.At(function) : OptionalFunctionIndex.Missing,
                    });
                    break;
                }
            case UnitQueryKind.ExceptionMetadata:
                value = reader.Bool()
                    ? new UnitLookup<ExceptionGroupMetadata>(true, new(reader.Int(), reader.Int(), reader.Bool()))
                    : new UnitLookup<ExceptionGroupMetadata>(false, default);
                break;
            case UnitQueryKind.RootMap:
                value = reader.Bool()
                    ? new UnitLookup<MethodRootMap>(true, RootMap(reader))
                    : new UnitLookup<MethodRootMap>(false, null);
                break;
            case UnitQueryKind.ManagedCallSite:
                value = Lookup(reader, reader =>
                {
                    var siteKey = new ManagedCallSiteKey(new ManagedMethodIdentity(Text(reader, caller)), reader.Int());
                    var operation = (ManagedCallOperation)reader.Byte();
                    var method = reader.Method();
                    var constrained = reader.Type();
                    var target = new ManagedMethodIdentity(Text(reader, method.CanonicalName));
                    return new ManagedCallSite(siteKey, operation, target, method, constrained);
                });
                break;
            case UnitQueryKind.DispatchCallSite:
                value = Lookup(reader, reader =>
                {
                    var siteCaller = Text(reader, caller);
                    var offset = reader.Int();
                    var declaration = reader.Method();
                    var count = reader.Int();
                    var targets = ImmutableArray.CreateBuilder<DispatchTargetModel>(count);
                    for (var index = 0; index < count; index++)
                    {
                        var receiver = reader.Type()!;
                        targets.Add(new(receiver, reader.Method()));
                    }
                    return new DispatchCallSiteModel(siteCaller, offset, declaration, targets.MoveToImmutable());
                });
                break;
            case UnitQueryKind.TypeTestSite:
                value = Lookup(reader, reader =>
                {
                    var siteCaller = Text(reader, caller);
                    var offset = reader.Int();
                    var target = reader.Type()!;
                    return new TypeTestSiteModel(siteCaller, offset, target, reader.Types());
                });
                break;
            case UnitQueryKind.ByRefEffects:
                value = Lookup(reader, static reader =>
                {
                    var written = Set(reader);
                    var escaped = Set(reader);
                    return new ManagedByRefMethodEffects(written, escaped, Set(reader));
                });
                break;
            case UnitQueryKind.TypeDescriptors:
                value = Array(reader, static reader =>
                {
                    var type = reader.Entity();
                    var (typeId, baseTypeId, size, bitmap, bits, finalizer) = Descriptor(reader);
                    return new TypeDescriptorLayout(type, typeId, baseTypeId, size, bitmap, bits, finalizer)
                    {
                        AssignableTypeIdsAddress = reader.Int(),
                        AssignableTypeIdCount = reader.Int(),
                        IsInterface = reader.Bool(),
                    };
                });
                break;
            case UnitQueryKind.ConstructedTypeDescriptors:
                value = Array(reader, static reader =>
                {
                    var type = reader.Type()!;
                    var (typeId, baseTypeId, size, bitmap, bits, finalizer) = Descriptor(reader);
                    return new ConstructedTypeDescriptorLayout(type, typeId, baseTypeId, size, bitmap, bits, finalizer)
                    {
                        AssignableTypeIdsAddress = reader.Int(),
                        AssignableTypeIdCount = reader.Int(),
                        IsInterface = reader.Bool(),
                    };
                });
                break;
            case UnitQueryKind.ValueTypeDescriptors:
                value = Array(reader, static reader => new ValueTypeDescriptorLayout(
                    reader.Type()!, reader.Int(), reader.Int(), reader.Int(), reader.Int(), reader.Int()));
                break;
            case UnitQueryKind.MetadataTypeDescriptors:
                value = Array(reader, static reader => new MetadataTypeDescriptorLayout(reader.Type()!, reader.Int()));
                break;
            case UnitQueryKind.EnumMetadata:
                value = Array(reader, static reader =>
                {
                    var type = reader.Entity();
                    var enumType = reader.Type()!;
                    var typeId = reader.Int();
                    var address = reader.Int();
                    var underlying = reader.Type()!;
                    var isFlags = reader.Bool();
                    var isOpen = reader.Bool();
                    var count = reader.Int();
                    var members = ImmutableArray.CreateBuilder<EnumMetadataMemberLayout>(count);
                    for (var index = 0; index < count; index++)
                    {
                        var name = reader.String()!;
                        var raw = (ulong)reader.Long();
                        members.Add(new(name, raw, new StringLayout(reader.Int(), reader.Int(), reader.Int())));
                    }
                    return new EnumMetadataLayout(type, enumType, typeId, address, underlying, isFlags, isOpen,
                        members.MoveToImmutable());
                });
                break;
            case UnitQueryKind.CallableMethods:
                {
                    // In the order they were written, as the compile enumerated them.
                    var count = reader.Int();
                    var callable = new Dictionary<string, MethodInstanceModel>(count, StringComparer.Ordinal);
                    for (var index = 0; index < count; index++)
                    {
                        var name = reader.String()!;
                        callable.Add(name, reader.Method());
                    }
                    value = callable;
                    break;
                }
            case UnitQueryKind.DelegateTypes:
                value = reader.Types().ToList();
                break;
            default:
                throw new NotSupportedException($"Answers of kind {kind} are not kept.");
        }
        if (!reader.AtEnd) throw new InvalidDataException($"A unit answer of kind {kind} is longer than it reads.");
        return value;
    }

    // The caller a site query's key names: the text before its "@offset".
    private static string? Caller(string key) =>
        key.LastIndexOf('@') is var at and > 0 ? key[..at] : null;

    // A text that is usually a known one: a flag, and the text only when it differs.
    private static void Text(UnitModelWriter writer, string text, string? usual)
    {
        var same = string.Equals(text, usual, StringComparison.Ordinal);
        writer.Bool(same);
        if (!same) writer.String(text);
    }

    private static string Text(UnitModelReader reader, string? usual) =>
        reader.Bool()
            ? usual ?? throw new InvalidDataException("A unit answer refers to a text its key does not hold.")
            : reader.String() ?? throw new InvalidDataException("A unit answer's text is missing.");

    private static void Lookup<T>(UnitModelWriter writer, UnitLookup<T> lookup, Action<UnitModelWriter, T> write)
    {
        writer.Bool(lookup.Found);
        if (lookup.Found) write(writer, lookup.Value!);
    }

    private static UnitLookup<T> Lookup<T>(UnitModelReader reader, Func<UnitModelReader, T> read) =>
        reader.Bool() ? new(true, read(reader)) : new(false, default);

    private static void Array<T>(UnitModelWriter writer, ImmutableArray<T> items, Action<UnitModelWriter, T> write)
    {
        writer.Int(items.Length);
        foreach (var item in items) write(writer, item);
    }

    private static ImmutableArray<T> Array<T>(UnitModelReader reader, Func<UnitModelReader, T> read)
    {
        var count = reader.Int();
        var items = ImmutableArray.CreateBuilder<T>(count);
        for (var index = 0; index < count; index++) items.Add(read(reader));
        return items.MoveToImmutable();
    }

    // A set's members in ascending order, so equal sets encode alike.
    private static void Set(UnitModelWriter writer, ImmutableHashSet<int> set)
    {
        writer.Int(set.Count);
        foreach (var member in set.Order()) writer.Int(member);
    }

    private static ImmutableHashSet<int> Set(UnitModelReader reader)
    {
        var count = reader.Int();
        var set = ImmutableHashSet.CreateBuilder<int>();
        for (var index = 0; index < count; index++) set.Add(reader.Int());
        return set.ToImmutable();
    }

    private static void Descriptor(UnitModelWriter writer, int typeId, int baseTypeId, int size, int bitmap, int bits,
        MethodInstanceModel? finalizer, int assignable, int assignableCount, bool isInterface)
    {
        writer.Int(typeId);
        writer.Int(baseTypeId);
        writer.Int(size);
        writer.Int(bitmap);
        writer.Int(bits);
        writer.Bool(finalizer is not null);
        if (finalizer is not null) writer.Method(finalizer);
        writer.Int(assignable);
        writer.Int(assignableCount);
        writer.Bool(isInterface);
    }

    private static (int TypeId, int BaseTypeId, int Size, int Bitmap, int Bits, MethodInstanceModel? Finalizer) Descriptor(
        UnitModelReader reader)
    {
        var typeId = reader.Int();
        var baseTypeId = reader.Int();
        var size = reader.Int();
        var bitmap = reader.Int();
        var bits = reader.Int();
        return (typeId, baseTypeId, size, bitmap, bits, reader.Bool() ? reader.Method() : null);
    }

    private static void Object(UnitModelWriter writer, ObjectLayout layout)
    {
        writer.Int(layout.TypeId);
        writer.Int(layout.Size);
        writer.Ints(layout.ReferenceOffsets);
        writer.Int(layout.LastSubtypeId);
    }

    private static ObjectLayout Object(UnitModelReader reader)
    {
        var typeId = reader.Int();
        var size = reader.Int();
        var references = reader.Ints();
        return new ObjectLayout(typeId, size, references) { LastSubtypeId = reader.Int() };
    }

    // A root map in its unit format, with its anchors' backing types written structurally.
    private static void RootMap(UnitModelWriter writer, MethodRootMap map)
    {
        writer.Entity(map.Method);
        writer.Bytes(UnitRootMapFormat.Write(map).AsSpan());
        writer.Int(map.AnchorPlan.Anchors.Length);
        foreach (var anchor in map.AnchorPlan.Anchors) writer.Type(anchor.BackingType);
    }

    private static MethodRootMap RootMap(UnitModelReader reader)
    {
        var method = reader.Entity();
        var format = reader.Bytes();
        var count = reader.Int();
        var types = new Dictionary<string, CliTypeIdentity>(count, StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var type = reader.Type()!;
            types[type.CanonicalName] = type;
        }
        return UnitRootMapFormat.Read(format, method, name => types.GetValueOrDefault(name))
            ?? throw new InvalidDataException("A unit answer's root map names an unknown anchor type.");
    }
}
