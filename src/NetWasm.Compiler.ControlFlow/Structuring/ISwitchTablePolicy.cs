// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Whether a switch dispatches through a table rather than its chain of compares.
internal interface ISwitchTablePolicy
{
    bool Tables(int cases, int targets);
}

// A table costs about one indirect branch whatever its size, where the chain costs a compare per case it passes
// (Wasmtime, measured with compiler-qualification/switch-dispatch): the table breaks even at 16 cases on values no
// branch predictor learns and wins past them. Each distinct target is a nested block, and compiling a table's blocks
// grows faster than their number past about 16,000 (Wasmtime 0.44 s at 16,000, 7.9 s at 64,000).
internal sealed class SwitchTablePolicy(int minimumCases, int maximumTargets) : ISwitchTablePolicy
{
    public bool Tables(int cases, int targets) => cases >= minimumCases && targets <= maximumTargets;
}
