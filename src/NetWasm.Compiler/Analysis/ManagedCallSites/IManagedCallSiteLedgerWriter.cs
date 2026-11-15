// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis.ManagedCallSites;

internal interface IManagedCallSiteLedgerWriter
{
    void Write(ReachabilityLedger ledger, ImmutableArray<ManagedCallSite> callSites);
}
