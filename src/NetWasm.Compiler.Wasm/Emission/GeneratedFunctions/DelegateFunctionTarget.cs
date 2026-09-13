using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record DelegateTraversalFunctionIndices(
    OptionalFunctionIndex Count,
    OptionalFunctionIndex Leaf);

internal sealed record DelegateHelperTarget(
    ImmutableArray<CliTypeIdentity> DelegateTypes,
    DelegateTraversalFunctionIndices Indices);
