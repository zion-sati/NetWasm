// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis.ManagedCallSites;

internal sealed class ManagedCallSiteLedgerWriter : IManagedCallSiteLedgerWriter
{
    public void Write(ReachabilityLedger ledger, ImmutableArray<ManagedCallSite> callSites)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (callSites.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var callSite in callSites)
        {
            if (!ledger.ManagedCallSites.TryGetValue(callSite.Key, out var existing))
            {
                ledger.ManagedCallSites.Add(callSite.Key, callSite);
                continue;
            }

            if (existing != callSite)
            {
                throw new InvalidOperationException(
                    "A managed call site cannot have more than one canonical meaning.");
            }
        }
    }
}
