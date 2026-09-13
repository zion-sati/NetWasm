using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityInstructionAnalyzerFactory
{
    IReachabilityInstructionAnalyzer Create(
        ITypeFinder typeFinder,
        ITypeIdentityResolver typeIdentities,
        ICalledMethodResolver calledMethods,
        ITypeOperandResolver typeOperands,
        IDispatchSiteKeyBuilder dispatchSiteKeys,
        IDelegateMethodClassifier delegateMethods);
}
