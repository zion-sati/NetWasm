using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record WasmMethodLoweringResult(
    ImmutableDictionary<EntityKey, StructuredMethod> Methods,
    ImmutableDictionary<string, StructuredMethod> ConstructedMethods);
