// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>A Preview 2 clock explicitly granted to the guest.</summary>
public enum NetWasmClock
{
    Wall,
    Monotonic,
}
