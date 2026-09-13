using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableProgramBuilderFactory : IReachableProgramBuilderFactory
{
    public IReachableProgramBuilder Create(
        IAllocationCapabilityAnalyzer allocationCapabilities,
        IDelegateBindingPlanner delegateBindings) =>
        new ReachableProgramBuilder(allocationCapabilities, delegateBindings);
}
