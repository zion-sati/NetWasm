// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.ReceiverFlow;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// The closure builds after the first replay its admissions. A reduction rewrites bodies, and inlining after it can
// move a callee's call into its caller: a method keeps its calls' admissions only where its gated calls (offset and
// declaration) are exactly the first build's; otherwise its calls take their slot use's union.
public sealed class ClosureReceiverFlowReplayTests
{
    private static readonly CliTypeIdentity Sequence = CliTypeIdentity.Named(new("Test"), "Test", "Seq", false);
    private static readonly CliTypeIdentity ArraySeq = CliTypeIdentity.Named(new("Test"), "Test", "ArraySeq", false);
    private static readonly CliTypeIdentity ListSeq = CliTypeIdentity.Named(new("Test"), "Test", "ListSeq", false);
    private static readonly MethodInstanceModel MapToString = Map(CliTypeIdentity.Named(new("Test"), "Test", "Text", false));
    private static readonly MethodInstanceModel MapToNumber = Map(CliTypeIdentity.Named(new("Test"), "Test", "Number", false));

    // The first build: Caller maps to text at 20 (array receivers) and to numbers at 31 (list receivers).
    private static ClosureReceiverFlow.Admissions FirstBuild() => new(
        new Dictionary<DispatchSlot, (HashSet<CliTypeIdentity> Receivers, List<FlowBound> Bounds)>(),
        new Dictionary<string, ClosureReceiverFlow.MethodCalls>
        {
            ["Caller"] = new(
                [(20, MapToString.CanonicalName), (31, MapToNumber.CanonicalName)],
                new Dictionary<string, ReceiverFlowAdmission>
                {
                    ["Caller@20"] = new([ArraySeq], Bounded: false),
                    ["Caller@31"] = new([ListSeq], Bounded: false),
                }),
        });

    [Fact]
    public void AMethodWhoseGatedCallsAreUnchangedKeepsEachCallsAdmission()
    {
        var ledger = new ReachabilityLedger();
        ledger.DispatchDeclarations.Add("Caller@20", Call("Caller", 20, MapToString));
        ledger.DispatchDeclarations.Add("Caller@31", Call("Caller", 31, MapToNumber));
        ledger.DispatchDeclarations.Add("Caller@40", Call("Caller", 40, MapToString, CilOperation.Call));
        ledger.DispatchDeclarations.Add("Other@20", Call("Other", 20, MapToString));

        var sites = ClosureReceiverFlow.Replay(FirstBuild(), static (_, _) => false, ledger, ReceiverFlowDispatchPolicy.EveryVirtualCall).SiteAdmissions();

        Assert.Equal([ArraySeq], sites["Caller@20"].Receivers);
        Assert.Equal([ListSeq], sites["Caller@31"].Receivers);
        Assert.False(sites.ContainsKey("Caller@40"), "a direct call took a generic virtual admission");
        Assert.False(sites.ContainsKey("Other@20"), "a method the first build did not record took an admission");
    }

    [Theory]
    [InlineData("moved")]
    [InlineData("inlined")]
    [InlineData("removed")]
    public void AMethodWhoseGatedCallsChangedTakesItsSlotUsesUnion(string change)
    {
        var ledger = new ReachabilityLedger();
        switch (change)
        {
            case "moved":
                // A reduction removed code before the calls: the text call now sits where the number call was.
                ledger.DispatchDeclarations.Add("Caller@20", Call("Caller", 20, MapToNumber));
                ledger.DispatchDeclarations.Add("Caller@9", Call("Caller", 9, MapToString));
                break;
            case "inlined":
                // A callee's call inlined into the caller, beside the caller's own two.
                ledger.DispatchDeclarations.Add("Caller@20", Call("Caller", 20, MapToString));
                ledger.DispatchDeclarations.Add("Caller@31", Call("Caller", 31, MapToNumber));
                ledger.DispatchDeclarations.Add("Caller@44", Call("Caller", 44, MapToString));
                break;
            default:
                ledger.DispatchDeclarations.Add("Caller@20", Call("Caller", 20, MapToString));
                break;
        }

        var sites = ClosureReceiverFlow.Replay(FirstBuild(), static (_, _) => false, ledger, ReceiverFlowDispatchPolicy.EveryVirtualCall).SiteAdmissions();

        Assert.Empty(sites);
    }

    [Fact]
    public void ReplayedGateHoldsUnadmittedReceiversOfKnownSlotsAndPassesUnknownSlots()
    {
        var known = new DispatchSlot(CilOperation.CallVirtual, MapToString.CanonicalName);
        var unknown = new DispatchSlot(CilOperation.CallVirtual, MapToNumber.CanonicalName);
        var admissions = new ClosureReceiverFlow.Admissions(
            new Dictionary<DispatchSlot, (HashSet<CliTypeIdentity> Receivers, List<FlowBound> Bounds)>
            {
                [known] = ([ArraySeq], []),
            },
            new Dictionary<string, ClosureReceiverFlow.MethodCalls>());

        var gate = ClosureReceiverFlow.Replay(admissions, static (_, _) => false, new ReachabilityLedger(), ReceiverFlowDispatchPolicy.EveryVirtualCall).Gate;

        Assert.False(gate.Holds(new(known, Call("Caller", 20, MapToString), ArraySeq)));
        Assert.True(gate.Holds(new(known, Call("Caller", 20, MapToString), ListSeq)));
        Assert.False(gate.Holds(new(unknown, Call("Caller", 31, MapToNumber), ListSeq)));
    }

    // Every virtual call waits for the flow under EveryVirtualCall; under GenericVirtualCalls only one with method
    // arguments does, and a plain virtual or interface call keeps its targets by type.
    [Theory]
    [InlineData(ReceiverFlowDispatchPolicy.GenericVirtualCalls, false)]
    [InlineData(ReceiverFlowDispatchPolicy.EveryVirtualCall, true)]
    public void ThePolicyNamesWhichVirtualCallsTheFlowGates(ReceiverFlowDispatchPolicy policy, bool gatesPlainCalls)
    {
        var plain = new MethodInstanceModel(
            new(new(new("Test"), 0x06000002), new EntityKey(new("Test"), 0x02000001), "Count", false, null!, 0)
            {
                IsVirtual = true,
            },
            Sequence, [], null!);

        Assert.True(ReceiverFlowDispatchGate.Gates(policy, CilOperation.CallVirtual, MapToString, null));
        Assert.Equal(gatesPlainCalls, ReceiverFlowDispatchGate.Gates(policy, CilOperation.CallVirtual, plain, null));
        Assert.False(ReceiverFlowDispatchGate.Gates(policy, CilOperation.Call, MapToString, null));
    }

    // The gate delays a slot use's candidates and must not reorder them: it releases them in the order the closure
    // filed them, across slot uses, as type-based dispatch queues them. The generic cycle cutoff counts on that order.
    [Fact]
    public void TheGateReleasesCandidatesInTheOrderTheClosureFiledThem()
    {
        var gate = new ReceiverFlowDispatchGate(static (_, _) => false, ReceiverFlowDispatchPolicy.EveryVirtualCall);
        var text = new DispatchSlot(CilOperation.CallVirtual, MapToString.CanonicalName);
        var number = new DispatchSlot(CilOperation.CallVirtual, MapToNumber.CanonicalName);
        Assert.True(gate.Holds(new(number, Call("Caller", 31, MapToNumber), ListSeq)));
        Assert.True(gate.Holds(new(text, Call("Caller", 20, MapToString), ListSeq)));
        Assert.True(gate.Holds(new(text, Call("Caller", 20, MapToString), ArraySeq)));
        Assert.True(gate.Holds(new(number, Call("Caller", 31, MapToNumber), ArraySeq)));

        var texts = gate.Admit(text, [ArraySeq, ListSeq], []);
        var numbers = gate.Admit(number, [ArraySeq, ListSeq], []);

        // By name ArraySeq comes first.
        Assert.Equal([ListSeq, ArraySeq], texts.Select(static item => item.Candidate.Receiver));
        Assert.Equal([ListSeq, ArraySeq], numbers.Select(static item => item.Candidate.Receiver));
        Assert.Equal([(number, ListSeq), (text, ListSeq), (text, ArraySeq), (number, ArraySeq)],
            texts.Concat(numbers).OrderBy(static item => item.Order)
                .Select(static item => (item.Candidate.Slot, item.Candidate.Receiver)));
    }

    private static MethodInstanceModel Map(CliTypeIdentity result) => new(
        new(new(new("Test"), 0x06000001), new EntityKey(new("Test"), 0x02000001), "Map", false, null!, 1)
        {
            IsVirtual = true,
        },
        Sequence, [result], null!);

    // A call of a method that is not virtual, or is final, has one target whatever reaches it, so the flow does not
    // gate it; a virtual one, generic or not, waits for the flow.
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void TheFlowGatesOnlyCallsWhoseTargetDependsOnTheReceiver(bool isVirtual, bool isFinal, bool gated)
    {
        var count = new MethodInstanceModel(
            new(new(new("Test"), 0x06000002), new EntityKey(new("Test"), 0x02000001), "Count", false, null!, 0)
            {
                IsVirtual = isVirtual,
                IsFinal = isFinal,
            },
            Sequence, [], null!);

        Assert.Equal(gated, ReceiverFlowDispatchGate.Gates(ReceiverFlowDispatchPolicy.EveryVirtualCall, CilOperation.CallVirtual, count, null));
        // Constrained to a value type, the type picks the implementation.
        Assert.False(ReceiverFlowDispatchGate.Gates(ReceiverFlowDispatchPolicy.EveryVirtualCall, CilOperation.CallVirtual, count,
            CliTypeIdentity.Named(new("Test"), "Test", "Point", true)));
        Assert.False(ReceiverFlowDispatchGate.Gates(ReceiverFlowDispatchPolicy.EveryVirtualCall, CilOperation.Call, count, null));
    }

    private static DispatchDeclaration Call(string caller, int offset, MethodInstanceModel declaration,
        CilOperation operation = CilOperation.CallVirtual) => new(caller, offset, declaration, operation);
}
