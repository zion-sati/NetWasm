// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// Names the method a lowering failure came from. Lowering reports what a method's IL cannot do as a diagnostic; any
// other exception is a compiler defect, reported against the method instead of as a bare message (issue #259). A unit
// replay miss is neither: the replay catches it to compile the method afresh, so it passes through as it is.
internal sealed class MethodAttributingWasmMethodLowerer(
    IWasmMethodLowerer inner,
    IMethodDefectExceptionFactory defects) : IWasmMethodLowerer
{
    private readonly IWasmMethodLowerer _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IMethodDefectExceptionFactory _defects = defects ?? throw new ArgumentNullException(nameof(defects));

    public StructuredMethod Lower(
        ManagedMethodBody method,
        BranchPrefixDuplicationPolicy branchPrefixDuplication =
            BranchPrefixDuplicationPolicy.SizeAware,
        DispatcherEliminationPolicy dispatcherElimination =
            DispatcherEliminationPolicy.Disabled)
    {
        ArgumentNullException.ThrowIfNull(method);
        try
        {
            return _inner.Lower(method, branchPrefixDuplication, dispatcherElimination);
        }
        catch (Exception exception) when (exception is not
            (CompilerException or OperationCanceledException or UnitReplayMissException))
        {
            throw _defects.Create(method.Method.Definition, method.Method.DeclaringType, "lowering", exception);
        }
    }
}
