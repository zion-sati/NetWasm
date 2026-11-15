// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum MulticastDispatchPolicy
{
    // A delegate type's multicast function calls its invoke function for each entry.
    Shared,
    // The multicast function dispatches each entry itself, a second copy of the invoke
    // function's dispatch that saves a call per entry.
    Inline,
}
