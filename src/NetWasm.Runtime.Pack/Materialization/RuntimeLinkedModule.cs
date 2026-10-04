using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeLinkedModule(
    RuntimeLinkedMemoryLayout MemoryLayout,
    ImmutableArray<RuntimeLinkedFunctionType> FunctionTypes,
    ImmutableArray<uint> ImportedFunctionTypeIndices,
    ImmutableArray<uint> DefinedFunctionTypeIndices,
    ImmutableArray<RuntimeLinkedExport> Exports)
{
    public ImmutableArray<RuntimeLinkedImport> Imports { get; init; } = [];
}

internal sealed record RuntimeLinkedImport(string Module, string Name, byte Kind, uint TypeIndex);

internal sealed record RuntimeLinkedFunctionType(ImmutableArray<byte> Parameters, ImmutableArray<byte> Results);

internal sealed record RuntimeLinkedExport(string Name, byte Kind, uint Index);
