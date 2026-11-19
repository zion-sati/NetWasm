// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

// When an unbounded virtual call on object (Equals, GetHashCode, ToString) calls CoreLib's shared body for it, which
// carries the one copy of the dispatch over every allocated type, instead of dispatching inline: much less code, one
// more call. Under whole-program analysis every such call has a target per allocated type, so the threshold is in
// effect a program size.
public enum ObjectDispatchPolicy
{
    // At 32 targets or more (the size presets).
    Shared,
    // At 1,024 targets or more (the speed presets): a large program shares, a small one keeps its calls inline.
    SharedAtLargeSites,
}

public static class ObjectDispatchPolicyThresholds
{
    // The fewest targets at which a call uses the shared body.
    public static int MinimumTargets(this ObjectDispatchPolicy policy) => policy switch
    {
        ObjectDispatchPolicy.Shared => 32,
        ObjectDispatchPolicy.SharedAtLargeSites => 1024,
        _ => throw new System.ArgumentOutOfRangeException(nameof(policy)),
    };
}
