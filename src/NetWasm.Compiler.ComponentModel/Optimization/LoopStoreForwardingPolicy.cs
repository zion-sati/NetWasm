// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopStoreForwardingPolicy
{
    Disabled,
    // A loop that updates one array or span element in place, and writes
    // memory no other way, reads the value it last stored from a local.
    Elements,
}
