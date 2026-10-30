using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IWasmMethodLowerer
{
    StructuredMethod Lower(
        ManagedMethodBody method,
        BranchPrefixDuplicationPolicy branchPrefixDuplication =
            BranchPrefixDuplicationPolicy.SizeAware,
        DispatcherEliminationPolicy dispatcherElimination =
            DispatcherEliminationPolicy.Disabled);
}
