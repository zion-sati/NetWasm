// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Threading
{
    // Whether the program has changed an ambient context: an AsyncLocal value, ExecutionContext flow suppression or a
    // SynchronizationContext it installed. Only those write it, so a program that reaches none of them reads its default
    // and the compiler folds away the context captures and restores that runtime async makes. Until the first change
    // the contexts are the defaults, which is what a capture made before it stands for.
    internal static class AmbientContextChanges
    {
        internal static bool Made;
    }
}
