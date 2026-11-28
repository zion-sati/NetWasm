// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A method Roslyn compiled to an async state machine: the machine type and its MoveNext.
internal readonly record struct AsyncStateMachineKickoff(TypeDefinitionHandle Machine, MethodDefinitionHandle MoveNext);

// A member of an async iterator's machine that compiles as runtime async: MoveNextAsync (Dispose false) or DisposeAsync,
// with the machine, its MoveNext and its MoveNextAsync.
internal readonly record struct AsyncIteratorMember(TypeDefinitionHandle Machine, MethodDefinitionHandle MoveNext,
    MethodDefinitionHandle MoveNextAsync, bool Dispose);

// The methods of an assembly that Roslyn compiled to an async state machine: each carries
// [AsyncStateMachine(typeof(<M>d__n))], the machine nested in the method's own type. Async iterators carry
// [AsyncIteratorStateMachine] instead and are not among them. One pass over the custom attributes; a declaring type's
// nested types are indexed by name once, when the first of its methods names a machine.
internal static class AsyncStateMachineKickoffs
{
    private const string AttributeNamespace = "System.Runtime.CompilerServices";
    private const string AttributeName = "AsyncStateMachineAttribute";
    private const string IteratorAttributeName = "AsyncIteratorStateMachineAttribute";

    private static readonly HashSet<string> Builders =
    [
        AttributeNamespace + ".AsyncTaskMethodBuilder", AttributeNamespace + ".AsyncTaskMethodBuilder`1",
        AttributeNamespace + ".AsyncValueTaskMethodBuilder", AttributeNamespace + ".AsyncValueTaskMethodBuilder`1",
    ];

    // Whether a builder is the async iterator builder whose machines the iterator lowering takes.
    public static bool TakesIteratorBuilder(CliTypeIdentity builder) =>
        builder.FullName == AttributeNamespace + ".AsyncIteratorMethodBuilder";

    // Whether a builder is one of the task builders a state machine rewrite takes; any other (a pooling or custom
    // builder, async void's) keeps its machine.
    public static bool TakesBuilder(CliTypeIdentity builder) =>
        (builder.Shape == CliTypeShape.GenericInstantiation ? builder.ElementType?.FullName : builder.FullName) is { } name &&
        Builders.Contains(name);

    public static Dictionary<MethodDefinitionHandle, AsyncStateMachineKickoff> Find(MetadataReader reader) =>
        Find(reader, AttributeName);

    // The MoveNextAsync and DisposeAsync of each machine an [AsyncIteratorStateMachine] method names (its
    // IAsyncEnumerator<T>.MoveNextAsync and IAsyncDisposable.DisposeAsync), by method.
    public static Dictionary<MethodDefinitionHandle, AsyncIteratorMember> FindIteratorMembers(MetadataReader reader)
    {
        var members = new Dictionary<MethodDefinitionHandle, AsyncIteratorMember>();
        foreach (var iterator in Find(reader, IteratorAttributeName).Values)
        {
            MethodDefinitionHandle moveNextAsync = default, disposeAsync = default;
            foreach (var candidate in reader.GetTypeDefinition(iterator.Machine).GetMethods())
            {
                var name = reader.GetString(reader.GetMethodDefinition(candidate).Name);
                if (name.EndsWith(".MoveNextAsync", StringComparison.Ordinal)) moveNextAsync = candidate;
                else if (name.EndsWith(".DisposeAsync", StringComparison.Ordinal)) disposeAsync = candidate;
            }
            if (moveNextAsync.IsNil || disposeAsync.IsNil) continue;
            members[moveNextAsync] = new(iterator.Machine, iterator.MoveNext, moveNextAsync, false);
            members[disposeAsync] = new(iterator.Machine, iterator.MoveNext, moveNextAsync, true);
        }
        return members;
    }

    private static Dictionary<MethodDefinitionHandle, AsyncStateMachineKickoff> Find(MetadataReader reader, string attributeName)
    {
        var kickoffs = new Dictionary<MethodDefinitionHandle, AsyncStateMachineKickoff>();
        Dictionary<TypeDefinitionHandle, Dictionary<string, TypeDefinitionHandle>>? nested = null;
        foreach (var handle in reader.CustomAttributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Parent.Kind != HandleKind.MethodDefinition ||
                !IsStateMachineAttribute(reader, attribute.Constructor, attributeName) ||
                MachineName(reader, attribute) is not { } name)
            {
                continue;
            }
            var method = (MethodDefinitionHandle)attribute.Parent;
            var declaring = reader.GetMethodDefinition(method).GetDeclaringType();
            nested ??= [];
            if (!nested.TryGetValue(declaring, out var byName))
            {
                byName = [];
                foreach (var type in reader.GetTypeDefinition(declaring).GetNestedTypes())
                    byName.TryAdd(reader.GetString(reader.GetTypeDefinition(type).Name), type);
                nested[declaring] = byName;
            }
            if (!byName.TryGetValue(name, out var machine)) continue;
            foreach (var candidate in reader.GetTypeDefinition(machine).GetMethods())
            {
                if (!reader.StringComparer.Equals(reader.GetMethodDefinition(candidate).Name, "MoveNext")) continue;
                kickoffs[method] = new(machine, candidate);
                break;
            }
        }
        return kickoffs;
    }

    private static bool IsStateMachineAttribute(MetadataReader reader, EntityHandle constructor, string attributeName)
    {
        var owner = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default(EntityHandle),
        };
        return owner.Kind switch
        {
            HandleKind.TypeReference when reader.GetTypeReference((TypeReferenceHandle)owner) is var type =>
                reader.StringComparer.Equals(type.Namespace, AttributeNamespace) && reader.StringComparer.Equals(type.Name, attributeName),
            HandleKind.TypeDefinition when reader.GetTypeDefinition((TypeDefinitionHandle)owner) is var type =>
                reader.StringComparer.Equals(type.Namespace, AttributeNamespace) && reader.StringComparer.Equals(type.Name, attributeName),
            _ => false,
        };
    }

    // The machine's own name: the attribute's one argument is the machine's serialized type name ("N.C+<M>d__3",
    // "N.C`1+<M>d__3`1"), its last nested segment the machine's name. Roslyn's names escape nothing.
    private static string? MachineName(MetadataReader reader, CustomAttribute attribute)
    {
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.Length < 3 || blob.ReadUInt16() != 1 || blob.ReadSerializedString() is not { } serialized) return null;
        var end = serialized.IndexOf(',');
        if (end >= 0) serialized = serialized[..end];
        if (serialized.Contains('\\')) return null;
        var separator = serialized.LastIndexOf('+');
        return separator < 0 ? null : serialized[(separator + 1)..];
    }
}
