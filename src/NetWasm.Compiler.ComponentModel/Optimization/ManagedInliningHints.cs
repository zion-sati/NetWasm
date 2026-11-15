// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

/// <summary>Controls managed annotations, not the backend's heuristic inlining.</summary>
public enum ManagedInliningHints
{
    Auto,
    IgnoreAggressive,
    Preserve,
}
