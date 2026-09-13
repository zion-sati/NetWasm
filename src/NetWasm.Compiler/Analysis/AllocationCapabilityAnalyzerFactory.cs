using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal sealed class AllocationCapabilityAnalyzerFactory(
    IRuntimeAllocationSafepointClassifier runtimeSafepoints) :
    IAllocationCapabilityAnalyzerFactory
{
    private readonly IRuntimeAllocationSafepointClassifier _runtimeSafepoints =
        runtimeSafepoints ?? throw new ArgumentNullException(nameof(runtimeSafepoints));

    public IAllocationCapabilityAnalyzer Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods) =>
        new AllocationCapabilityAnalyzer(
            types,
            fields,
            methods,
            _runtimeSafepoints);
}
