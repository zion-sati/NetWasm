using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record ModuleDataPlan(
    IReadOnlyDictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>
        ExceptionMetadata,
    ImmutableArray<FilterFunclet> FilterFunclets,
    ImmutableArray<DataSegment> DataSegments,
    ImmutableDictionary<string, StaticInitializerGuard> StaticInitializerGuards,
    int StaticDataEnd)
{
    public static ModuleDataPlan Empty { get; } = new(
        ImmutableDictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>.Empty,
        [],
        [],
        [],
        0);
}

internal sealed class ModuleDataPlanner(
    IStaticDataLayout layouts,
    ITypeLayoutProvider types,
    IExceptionGroupEnumerator exceptionGroups,
    IStructuredExceptionGroupKeyFactory exceptionGroupKeys) : IModuleDataPlanner
{
    public ModuleDataPlan Build(
        IEnumerable<StructuredMethodEmission> methods,
        IReadOnlyList<EntityKey> directInitializers,
        IReadOnlyList<string> constructedInitializers)
    {
        var metadata = new Dictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>();
        var segments = layouts.DataSegments.ToList();
        var filterFunclets = ImmutableArray.CreateBuilder<FilterFunclet>();
        var address = Align4(layouts.StaticDataEnd);

        foreach (var emission in methods)
        {
            foreach (var groupId in exceptionGroups.Enumerate(emission.Method))
            {
                var group = emission.Method.ExceptionGroups[groupId];
                address = AddExceptionGroup(
                    emission.Method,
                    emission.Identity,
                    group,
                    address,
                    metadata,
                    segments,
                    filterFunclets);
            }
        }

        address = Align4(address);
        var guards = ImmutableDictionary.CreateBuilder<string, StaticInitializerGuard>(
            StringComparer.Ordinal);
        foreach (var initializer in directInitializers
                     .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(key => key.MetadataToken))
        {
            var key = StaticInitializerGuard.KeyFor(initializer);
            guards.Add(key, new(address, initializer, null));
            segments.Add(new(address, [0, 0, 0, 0]));
            address += sizeof(int);
        }
        foreach (var initializer in constructedInitializers.Order(StringComparer.Ordinal))
        {
            guards.Add(initializer, new(address, null, initializer));
            segments.Add(new(address, [0, 0, 0, 0]));
            address += sizeof(int);
        }

        return new(
            metadata,
            filterFunclets.ToImmutable(),
            [.. segments],
            guards.ToImmutable(),
            address);
    }

    private int AddExceptionGroup(
        StructuredMethod method,
        ManagedMethodIdentity callerIdentity,
        StructuredExceptionGroup group,
        int address,
        Dictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata> metadata,
        List<DataSegment> segments,
        ImmutableArray<FilterFunclet>.Builder filterFunclets)
    {
        if (group.Clauses.Any(clause =>
                clause.Kind == CilExceptionRegionKind.Filter))
        {
            return AddFilteredGroup(
                method,
                callerIdentity,
                group,
                address,
                metadata,
                segments,
                filterFunclets);
        }

        var catches = group.Clauses
            .Where(clause => clause.Kind == CilExceptionRegionKind.Catch)
            .Select(clause => clause.CatchType!.Value)
            .ToArray();
        if (catches.Length == 0)
        {
            metadata.Add(exceptionGroupKeys.Create(method, group.Id), new(0, 0, false));
            return address;
        }

        var bytes = new byte[catches.Length * sizeof(int)];
        for (var index = 0; index < catches.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(int), sizeof(int)),
                types.GetObjectLayout(catches[index]).TypeId);
        }
        segments.Add(new(address, [.. bytes]));
        metadata.Add(exceptionGroupKeys.Create(method, group.Id), new(address, catches.Length, false));
        return checked(address + bytes.Length);
    }

    private int AddFilteredGroup(
        StructuredMethod method,
        ManagedMethodIdentity callerIdentity,
        StructuredExceptionGroup group,
        int address,
        Dictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata> metadata,
        List<DataSegment> segments,
        ImmutableArray<FilterFunclet>.Builder filterFunclets)
    {
        var bytes = new byte[group.Clauses.Length * 3 * sizeof(int)];
        for (var index = 0; index < group.Clauses.Length; index++)
        {
            var clause = group.Clauses[index];
            var entry = index * 3 * sizeof(int);
            switch (clause.Kind)
            {
                case CilExceptionRegionKind.Catch:
                    BinaryPrimitives.WriteInt32LittleEndian(
                        bytes.AsSpan(entry + sizeof(int), sizeof(int)),
                        types.GetObjectLayout(clause.CatchType!.Value).TypeId);
                    break;
                case CilExceptionRegionKind.Filter:
                    var funcletId = filterFunclets.Count + 1;
                    BinaryPrimitives.WriteInt32LittleEndian(
                        bytes.AsSpan(entry, sizeof(int)),
                        1);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        bytes.AsSpan(entry + sizeof(int), sizeof(int)),
                        funcletId);
                    filterFunclets.Add(new(funcletId, callerIdentity,
                    method, clause));
                    break;
                default:
                    throw UnsupportedExceptionShape(
                        method,
                        "a filtered exception group may contain only filters and catches");
            }
        }

        segments.Add(new(address, [.. bytes]));
        metadata.Add(exceptionGroupKeys.Create(method, group.Id), new(
            address,
            unchecked((int)(0x80000000u | (uint)group.Clauses.Length)),
            true));
        return checked(address + bytes.Length);
    }

    private static CompilerException UnsupportedExceptionShape(
        StructuredMethod method,
        string message) => new(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedCil,
            message,
            method.Header.Method.Name));

    private static int Align4(int value) => checked((value + 3) & ~3);
}
