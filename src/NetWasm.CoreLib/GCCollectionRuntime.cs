// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System;

// The runtime's collection entries. The compiler replaces each call with a call into the runtime.
internal static class GCCollectionRuntime
{
    internal static void Collect()
    {
    }

    // A collection of the youngest generation alone.
    internal static void CollectYoung()
    {
    }

    // An event loop's idle time: the collection the heap is heading for, when it has grown enough since the last
    // one for a collection to be worth its while. Whether it collected.
    internal static bool CollectWhenIdle() => false;

    // Once the program has stayed quiet: a collection whatever the heap has grown by. 0 when it did not collect,
    // otherwise 1 plus the share of the heap it freed, in 255ths.
    internal static int CollectWhenQuiet() => 0;
}
