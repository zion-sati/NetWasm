using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DispatchCandidateIndex(ITypeRelationshipClassifier relationships)
    : IDispatchCandidateIndex
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly Dictionary<string, IndexedDeclaration> _declarations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<CliTypeIdentity, ContractGroup> _groups = [];
    private readonly List<ContractGroup> _groupOrder = [];
    private readonly HashSet<CliTypeIdentity> _receiverSet = [];
    private readonly List<CliTypeIdentity> _receiverOrder = [];
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
            _groupOrder.Add(group);
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
            result.Add(new DispatchCandidate(
                indexed.DispatchKey,
                indexed.Declaration,
                receiver));
        }

        return result.MoveToImmutable();
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
        foreach (var group in _groupOrder)
        {
            if (!_relationships.Classify(candidate.Receiver, group.Contract).IsHierarchyAssignable)
            {
                continue;
            }

            compatibleGroups.Add(group);
            declarations.AddRange(group.Declarations);
        }

        declarations.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
        _receiverSet.Add(candidate.Receiver);
        _receiverOrder.Add(candidate.Receiver);
        foreach (var group in compatibleGroups)
        {
            group.CompatibleReceivers.Add(candidate.Receiver);
        }

        var result = ImmutableArray.CreateBuilder<DispatchCandidate>(declarations.Count);
        foreach (var declaration in declarations)
        {
            result.Add(new DispatchCandidate(
                declaration.DispatchKey,
                declaration.Declaration,
                candidate.Receiver));
        }

        return result.MoveToImmutable();
    }

    private ContractGroup CreateGroup(CliTypeIdentity contract)
    {
        var compatibleReceivers = new List<CliTypeIdentity>();
        foreach (var receiver in _receiverOrder)
        {
            if (_relationships.Classify(receiver, contract).IsHierarchyAssignable)
            {
                compatibleReceivers.Add(receiver);
            }
        }

        return new ContractGroup(contract, compatibleReceivers);
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
