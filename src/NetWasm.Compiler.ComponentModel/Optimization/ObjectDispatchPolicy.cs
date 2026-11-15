// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ObjectDispatchPolicy
{
    // Each virtual call on object dispatches itself.
    Inline,
    // An unbounded virtual call on object with many targets calls CoreLib's shared body for it, which carries the
    // one copy of the dispatch over every allocated type: much less code, one more call.
    Shared,
}
