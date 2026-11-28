// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// The (slot use, receiver) pairs the closure resolves. A slot use (DispatchSlot) is resolved for a receiver once,
// when a site of it first admits the receiver: an unbounded site admits every receiver its method's declaring type
// admits, a bounded one those assignable to its bound, variance included. So a receiver costs one pair per slot use
// that admits it, however many sites call the slot use, and a further site of a slot use costs a pair only for each
// receiver its bound admits and no earlier site's did.
// Receivers are filed under every key they reach and contract groups under their own, so a new receiver or contract
// is classified only against those its keys can match.
internal sealed class DispatchCandidateIndex(
    ITypeRelationshipClassifier relationships,
    ITypeRelationshipKeyResolver relationshipKeys)
    : IDispatchCandidateIndex
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly ITypeRelationshipKeyResolver _relationshipKeys =
        relationshipKeys ?? throw new ArgumentNullException(nameof(relationshipKeys));
    private readonly Dictionary<string, List<(int Order, CliTypeIdentity Receiver)>> _receiversByKey =
        new(StringComparer.Ordinal);
    private readonly List<(int Order, CliTypeIdentity Receiver)> _unboundedReceivers = [];
    private readonly List<CliTypeIdentity> _receiverOrder = [];
    private readonly Dictionary<string, List<ContractGroup>> _groupsByKey = new(StringComparer.Ordinal);
    private readonly List<ContractGroup> _unboundedGroups = [];
    private readonly List<ContractGroup> _groupOrder = [];
    private readonly Dictionary<string, DispatchDeclaration> _sites = new(StringComparer.Ordinal);
    private readonly Dictionary<DispatchSlot, SlotUse> _slots = [];
    private readonly Dictionary<CliTypeIdentity, ContractGroup> _groups = [];
    private readonly HashSet<CliTypeIdentity> _receiverSet = [];
    private int _nextSlotOrdinal;

    public ImmutableArray<DispatchCandidate> Index(DispatchDeclarationCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.DispatchKey);
        ArgumentNullException.ThrowIfNull(candidate.Declaration);

        if (_sites.TryGetValue(candidate.DispatchKey, out var existing))
        {
            if (existing == candidate.Declaration)
            {
                return [];
            }

            throw new InvalidOperationException(
                $"Dispatch key '{candidate.DispatchKey}' identifies conflicting declarations.");
        }

        var declaration = candidate.Declaration;
        var contract = declaration.Declaration.DeclaringType;
        if (!_groups.TryGetValue(contract, out var group))
        {
            group = CreateGroup(contract);
            _groups.Add(contract, group);
            FileGroup(group);
        }
        _sites.Add(candidate.DispatchKey, declaration);

        var key = new DispatchSlot(declaration.Operation, declaration.Declaration.CanonicalName);
        if (!_slots.TryGetValue(key, out var slot))
        {
            slot = new SlotUse(_nextSlotOrdinal++, key, declaration);
            _slots.Add(key, slot);
            group.Slots.Add(slot);
        }
        if (slot.Unbounded)
        {
            return [];
        }
        var bound = declaration.ReceiverBound;
        if (bound is null)
        {
            slot.Unbounded = true;
        }
        else if (!slot.Bounds.Add(bound))
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<DispatchCandidate>();
        foreach (var receiver in group.CompatibleReceivers)
        {
            if (!slot.Admitted.Contains(receiver) && (bound is null || Admits(bound, receiver)))
            {
                slot.Admitted.Add(receiver);
                result.Add(new DispatchCandidate(slot.Key, slot.FirstSite, receiver));
            }
        }

        return result.ToImmutable();
    }

    public ImmutableArray<DispatchCandidate> Index(DispatchReceiverCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate.Receiver);
        if (_receiverSet.Contains(candidate.Receiver))
        {
            return [];
        }

        var slots = new List<SlotUse>();
        var compatibleGroups = new List<ContractGroup>();
        foreach (var group in GroupsFor(candidate.Receiver))
        {
            if (!_relationships.Classify(candidate.Receiver, group.Contract).IsAssignmentCompatible)
            {
                continue;
            }

            compatibleGroups.Add(group);
            slots.AddRange(group.Slots);
        }

        slots.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
        _receiverSet.Add(candidate.Receiver);
        FileReceiver(candidate.Receiver);
        foreach (var group in compatibleGroups)
        {
            group.CompatibleReceivers.Add(candidate.Receiver);
        }

        var result = ImmutableArray.CreateBuilder<DispatchCandidate>(slots.Count);
        foreach (var slot in slots)
        {
            if (slot.Unbounded || slot.Bounds.Any(bound => Admits(bound, candidate.Receiver)))
            {
                slot.Admitted.Add(candidate.Receiver);
                result.Add(new DispatchCandidate(slot.Key, slot.FirstSite, candidate.Receiver));
            }
        }

        return result.ToImmutable();
    }

    // A call site whose receivers all have a known static type reaches only the receivers assignable to it, variance
    // included.
    private bool Admits(CliTypeIdentity bound, CliTypeIdentity receiver) =>
        _relationships.Classify(receiver, bound).IsAssignmentCompatible;

    private ContractGroup CreateGroup(CliTypeIdentity contract)
    {
        var compatibleReceivers = new List<CliTypeIdentity>();
        foreach (var receiver in ReceiversFor(contract))
        {
            if (_relationships.Classify(receiver, contract).IsAssignmentCompatible)
            {
                compatibleReceivers.Add(receiver);
            }
        }

        return new ContractGroup(contract, compatibleReceivers);
    }

    private void FileReceiver(CliTypeIdentity receiver)
    {
        var entry = (_receiverOrder.Count, receiver);
        _receiverOrder.Add(receiver);
        var keys = _relationshipKeys.Resolve(receiver);
        if (keys.Unbounded)
        {
            _unboundedReceivers.Add(entry);
            return;
        }
        foreach (var key in keys.Reachable)
        {
            if (!_receiversByKey.TryGetValue(key, out var filed))
            {
                _receiversByKey.Add(key, filed = []);
            }
            filed.Add(entry);
        }
    }

    // The receivers that may be assignable to the contract, in the order they arrived.
    private IEnumerable<CliTypeIdentity> ReceiversFor(CliTypeIdentity contract)
    {
        var keys = _relationshipKeys.Resolve(contract);
        if (keys.Unbounded)
        {
            return _receiverOrder;
        }
        var filed = _receiversByKey.GetValueOrDefault(keys.Own) ?? [];
        return filed.Concat(_unboundedReceivers)
            .OrderBy(entry => entry.Order)
            .Select(entry => entry.Receiver);
    }

    private void FileGroup(ContractGroup group)
    {
        _groupOrder.Add(group);
        var keys = _relationshipKeys.Resolve(group.Contract);
        if (keys.Unbounded)
        {
            _unboundedGroups.Add(group);
        }
        else if (_groupsByKey.TryGetValue(keys.Own, out var filed))
        {
            filed.Add(group);
        }
        else
        {
            _groupsByKey.Add(keys.Own, [group]);
        }
    }

    // The contract groups the receiver may be assignable to.
    private IEnumerable<ContractGroup> GroupsFor(CliTypeIdentity receiver)
    {
        var keys = _relationshipKeys.Resolve(receiver);
        return keys.Unbounded
            ? _groupOrder
            : keys.Reachable
                .SelectMany(key => _groupsByKey.GetValueOrDefault(key) ?? [])
                .Concat(_unboundedGroups);
    }

    // A slot use, its sites' bounds (none once an unbounded site calls it) and the receivers some site of it admits.
    private sealed class SlotUse(int ordinal, DispatchSlot key, DispatchDeclaration firstSite)
    {
        public int Ordinal { get; } = ordinal;

        public DispatchSlot Key { get; } = key;

        public DispatchDeclaration FirstSite { get; } = firstSite;

        public bool Unbounded { get; set; }

        public HashSet<CliTypeIdentity> Bounds { get; } = [];

        public HashSet<CliTypeIdentity> Admitted { get; } = [];
    }

    private sealed class ContractGroup(
        CliTypeIdentity contract,
        List<CliTypeIdentity> compatibleReceivers)
    {
        public CliTypeIdentity Contract { get; } = contract;

        public List<SlotUse> Slots { get; } = [];

        public List<CliTypeIdentity> CompatibleReceivers { get; } = compatibleReceivers;
    }
}
