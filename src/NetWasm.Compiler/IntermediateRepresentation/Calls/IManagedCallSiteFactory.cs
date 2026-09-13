using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.IntermediateRepresentation.Calls;

internal interface IManagedCallSiteFactory
{
    ManagedCallSite Create(
        MethodInstanceModel caller,
        CilMethodBody body,
        int instructionIndex,
        MethodInstanceModel target,
        ITypeOperandResolver typeOperands);
}
