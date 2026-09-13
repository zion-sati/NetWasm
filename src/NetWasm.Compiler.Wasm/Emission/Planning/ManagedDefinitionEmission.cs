using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record ManagedDefinitionEmission(
    ManagedMethodIdentity Identity,
    EntityKey MethodKey);
