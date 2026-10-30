using NetWasm.Compiler.Analysis;
using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class CachingWasmMethodLowerer(
    IWasmMethodLowerer inner,
    IFrontendStructuredMethodRestorer restorer,
    IFrontendArtifactStager stager) : IWasmMethodLowerer
{
    private readonly IWasmMethodLowerer _inner = inner ??
        throw new ArgumentNullException(nameof(inner));
    private readonly IFrontendStructuredMethodRestorer _restorer = restorer ??
        throw new ArgumentNullException(nameof(restorer));
    private readonly IFrontendArtifactStager _stager = stager ??
        throw new ArgumentNullException(nameof(stager));

    public StructuredMethod Lower(
        ManagedMethodBody method,
        BranchPrefixDuplicationPolicy branchPrefixDuplication =
            BranchPrefixDuplicationPolicy.SizeAware,
        DispatcherEliminationPolicy dispatcherElimination =
            DispatcherEliminationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (_restorer.TryRestore(method.Method, out var structured))
        {
            return structured;
        }
        structured = _inner.Lower(method, branchPrefixDuplication, dispatcherElimination);
        _stager.Stage(method.Method, structured);
        return structured;
    }
}
