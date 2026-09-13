using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

internal interface IManagedCallSiteResolver
{
    ManagedCallSite Resolve(InstructionEmissionRequest request);
}
