// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedCallSiteLedgerWriterTests
{
    private readonly ManagedCallSiteLedgerWriter _writer = new();

    [Fact]
    public void WriteIgnoresDefaultAndEmptyInput()
    {
        ReachabilityLedger ledger = new ReachabilityLedgerFactory().Create();

        ((IManagedCallSiteLedgerWriter)_writer).Write(ledger, default);
        ((IManagedCallSiteLedgerWriter)_writer).Write(ledger, []);

        Assert.Empty(ledger.ManagedCallSites);
    }

    [Fact]
    public void WriteAddsSitesAndTreatsEqualDuplicatesAsIdempotent()
    {
        ReachabilityLedger ledger = new ReachabilityLedgerFactory().Create();
        ManagedCallSite site = CreateSite(CreateMethod(1, "Target"));

        ((IManagedCallSiteLedgerWriter)_writer).Write(ledger, [site, site]);

        ManagedCallSite stored = Assert.Single(ledger.ManagedCallSites).Value;
        Assert.Equal(site, stored);
    }

    [Fact]
    public void WriteRejectsAConflictingSiteAtTheSameCanonicalLocation()
    {
        ReachabilityLedger ledger = new ReachabilityLedgerFactory().Create();
        ManagedCallSite first = CreateSite(CreateMethod(2, "First"));
        ManagedCallSite conflicting = CreateSite(CreateMethod(3, "Second"));

        ((IManagedCallSiteLedgerWriter)_writer).Write(ledger, [first]);

        Assert.Throws<InvalidOperationException>(() =>
            ((IManagedCallSiteLedgerWriter)_writer).Write(ledger, [conflicting]));
        Assert.Equal(first, Assert.Single(ledger.ManagedCallSites).Value);
    }

    [Fact]
    public void WriteRejectsANullLedger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IManagedCallSiteLedgerWriter)_writer).Write(null!, ImmutableArray<ManagedCallSite>.Empty));
    }

    private static ManagedCallSite CreateSite(MethodInstanceModel target) =>
        new(
            new ManagedCallSiteKey(new ManagedMethodIdentity("Tests.Caller"), 17),
            ManagedCallOperation.Direct,
            new ManagedMethodIdentity(target.CanonicalName),
            target,
            ConstrainedType: null);

    private static MethodInstanceModel CreateMethod(int token, string name)
    {
        var assembly = new AssemblyIdentity("Tests");
        var typeKey = new EntityKey(assembly, 100);
        var signature = new MethodSignatureModel(CliValueKind.Void, []);
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, token),
            typeKey,
            name,
            IsStatic: true,
            signature,
            RelativeVirtualAddress: 0);

        return new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Container", isValueType: false),
            ImmutableArray<CliTypeIdentity>.Empty,
            signature);
    }
}
