// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum RuntimeAsyncSuspensionPolicy
{
    // Every await of a runtime-async method may suspend: each method lowers from its own body alone, as the early
    // cutoff, which serves unedited methods from the last compile, needs.
    Local,
    // A direct await of a runtime-async method that cannot suspend (none of the methods it awaits directly can, and it
    // awaits nothing else) lowers as a plain call, and such a method has no frame or resumption path.
    WholeProgram,
}
