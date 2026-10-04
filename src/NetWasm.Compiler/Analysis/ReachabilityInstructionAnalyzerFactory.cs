using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityInstructionAnalyzerFactory(
    INullableTypeResolver nullableTypes,
    IManagedCallSiteFactory managedCallSites,
    INativeCallbackDeclarationValidator nativeCallbacks) :
    IReachabilityInstructionAnalyzerFactory
{
    public ReachabilityInstructionAnalyzerFactory(
        INullableTypeResolver nullableTypes,
        IManagedCallSiteFactory managedCallSites) : this(
            nullableTypes,
            managedCallSites,
            new NativeCallbackDeclarationValidator())
    {
    }

    public IReachabilityInstructionAnalyzer Create(
        ITypeFinder typeFinder,
        ITypeIdentityResolver typeIdentities,
        ICalledMethodResolver calledMethods,
        ITypeOperandResolver typeOperands,
        IDispatchSiteKeyBuilder dispatchSiteKeys,
        IDelegateMethodClassifier delegateMethods) =>
        new StaticInitializationDependencyDecorator(new ReachabilityInstructionAnalyzer(
            typeFinder,
            typeIdentities,
            calledMethods,
            typeOperands,
            dispatchSiteKeys,
            delegateMethods,
            nullableTypes,
            managedCallSites,
            nativeCallbacks), typeFinder);
}
