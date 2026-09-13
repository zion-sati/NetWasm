using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityInstructionAnalyzerFactory(
    INullableTypeResolver nullableTypes,
    IManagedCallSiteFactory managedCallSites) :
    IReachabilityInstructionAnalyzerFactory
{
    public IReachabilityInstructionAnalyzer Create(
        ITypeFinder typeFinder,
        ITypeIdentityResolver typeIdentities,
        ICalledMethodResolver calledMethods,
        ITypeOperandResolver typeOperands,
        IDispatchSiteKeyBuilder dispatchSiteKeys,
        IDelegateMethodClassifier delegateMethods) =>
        new ReachabilityInstructionAnalyzer(
            typeFinder,
            typeIdentities,
            calledMethods,
            typeOperands,
            dispatchSiteKeys,
            delegateMethods,
            nullableTypes,
            managedCallSites);
}
