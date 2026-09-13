using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record MethodEmissionLayout(
    MethodEmissionContext Context,
    ImmutableArray<CliValueKind> LocalTypes);
