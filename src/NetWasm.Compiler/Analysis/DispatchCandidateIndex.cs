using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchCandidateIndex(
    ITypeRelationshipClassifier relationships,
    ITypeRelationshipKeyResolver relationshipKeys)
    : IDispatchCandidateIndex
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly ITypeRelationshipKeyResolver _relationshipKeys =
        relationshipKeys ?? throw new ArgumentNullException(nameof(relationshipKeys));
    // Receivers filed under every key they reach and contract groups under their own, so a
    // new receiver or contract is classified only against those its keys can match.
    private readonly Dictionary<string, List<(int Order, CliTypeIdentity Receiver)>> _receiversByKey =
        new(StringComparer.Ordinal);
    private readonly List<(int Order, CliTypeIdentity Receiver)> _unboundedReceivers = [];
    private readonly List<CliTypeIdentity> _receiverOrder = [];
    private readonly Dictionary<string, List<ContractGroup>> _groupsByKey = new(StringComparer.Ordinal);
    private readonly List<ContractGroup> _unboundedGroups = [];
    private readonly List<ContractGroup> _groupOrder = [];
    private readonly Dictionary<string, IndexedDeclaration> _declarations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<CliTypeIdentity, ContractGroup> _groups = [];
    private readonly HashSet<CliTypeIdentity> _receiverSet = [];
    private int _nextDeclarationOrdinal;

    public ImmutableArray<DispatchCandidate> Index(DispatchDeclarationCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.DispatchKey);
        ArgumentNullException.ThrowIfNull(candidate.Declaration);

        if (_declarations.TryGetValue(candidate.DispatchKey, out var existing))
        {
            if (existing.Declaration == candidate.Declaration)
            {
                return [];
            }

            throw new InvalidOperationException(
                $"Dispatch key '{candidate.DispatchKey}' identifies conflicting declarations.");
        }

        var contract = candidate.Declaration.Declaration.DeclaringType;
        if (!_groups.TryGetValue(contract, out var group))
        {
            group = CreateGroup(contract);
            _groups.Add(contract, group);
            FileGroup(group);
        }

        var indexed = new IndexedDeclaration(
            _nextDeclarationOrdinal++,
            candidate.DispatchKey,
            candidate.Declaration);
        _declarations.Add(candidate.DispatchKey, indexed);
        group.Declarations.Add(indexed);

        var result = ImmutableArray.CreateBuilder<DispatchCandidate>(
            group.CompatibleReceivers.Count);
        foreach (var receiver in group.CompatibleReceivers)
        {
            if (Admits(indexed.Declaration, receiver))
            {
                result.Add(new DispatchCandidate(
                    indexed.DispatchKey,
                    indexed.Declaration,
                    receiver));
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

        var compatibleGroups = new List<ContractGroup>();
        var declarations = new List<IndexedDeclaration>();
        foreach (var group in GroupsFor(candidate.Receiver))
        {
            if (!_relationships.Classify(candidate.Receiver, group.Contract).IsAssignmentCompatible)
            {
                continue;
            }

            compatibleGroups.Add(group);
            declarations.AddRange(group.Declarations);
        }

        declarations.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
        _receiverSet.Add(candidate.Receiver);
        FileReceiver(candidate.Receiver);
        foreach (var group in compatibleGroups)
        {
            group.CompatibleReceivers.Add(candidate.Receiver);
        }

        var result = ImmutableArray.CreateBuilder<DispatchCandidate>(declarations.Count);
        foreach (var declaration in declarations)
        {
            if (Admits(declaration.Declaration, candidate.Receiver))
            {
                result.Add(new DispatchCandidate(
                    declaration.DispatchKey,
                    declaration.Declaration,
                    candidate.Receiver));
            }
        }

        return result.ToImmutable();
    }

    // A call site whose receivers all have a known static type reaches only
    // the receivers assignable to it, variance included.
    private bool Admits(DispatchDeclaration declaration, CliTypeIdentity receiver) =>
        declaration.ReceiverBound is not { } bound ||
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

    private sealed record IndexedDeclaration(
        int Ordinal,
        string DispatchKey,
        DispatchDeclaration Declaration);

    private sealed class ContractGroup(
        CliTypeIdentity contract,
        List<CliTypeIdentity> compatibleReceivers)
    {
        public CliTypeIdentity Contract { get; } = contract;

        public List<IndexedDeclaration> Declarations { get; } = [];

        public List<CliTypeIdentity> CompatibleReceivers { get; } = compatibleReceivers;
    }
}
