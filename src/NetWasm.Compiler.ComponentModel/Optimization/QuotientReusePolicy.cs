// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum QuotientReusePolicy
{
    Disabled,
    // A remainder of two values an earlier division divided is computed from that division's quotient.
    AvailableQuotients,
}
