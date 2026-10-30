using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ControlFlow.Structuring;

public interface IValidatedStructuredMethodBuilderFactory
{
    IValidatedStructuredMethodBuilder Create(
        BranchPrefixDuplicationPolicy branchPrefixDuplication =
            BranchPrefixDuplicationPolicy.SizeAware,
        DispatcherEliminationPolicy dispatcherElimination =
            DispatcherEliminationPolicy.Disabled);
}
