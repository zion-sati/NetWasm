// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis;

// Told each time reachability publishes an analyzed method, with what it has reached so far.
internal interface IReachabilityProgressObserver
{
    void Observe(ReachabilityLedger ledger);
}
