using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record StructuredMethodEmission(
    ManagedMethodIdentity Identity,
    StructuredMethod Method);
