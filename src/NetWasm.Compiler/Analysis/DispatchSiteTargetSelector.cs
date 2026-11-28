// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchSiteTargetSelector
{
    // The targets of a site's slot use that the site's receiver bound admits, in the slot use's order: all of them for
    // an unbounded site.
    ImmutableArray<DispatchTargetModel> Select(
        CliTypeIdentity? receiverBound,
        ImmutableArray<DispatchTargetModel> slotTargets);
}

// A target's receiver type is the receiver it was resolved for, so a bounded site reaches the slot use's targets whose
// receivers are assignable to its bound, variance included, as the closure admits receivers for it. Only a receiver
// whose relationship keys reach the bound's own key (or either's keys are unbounded) can be, so the others are not
// classified, as the candidate index files them.
internal sealed class DispatchSiteTargetSelector(
    ITypeRelationshipClassifier relationships,
    ITypeRelationshipKeyResolver relationshipKeys) : IDispatchSiteTargetSelector
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly ITypeRelationshipKeyResolver _relationshipKeys =
        relationshipKeys ?? throw new ArgumentNullException(nameof(relationshipKeys));

    public ImmutableArray<DispatchTargetModel> Select(
        CliTypeIdentity? receiverBound,
        ImmutableArray<DispatchTargetModel> slotTargets)
    {
        if (receiverBound is null)
        {
            return slotTargets;
        }
        var bound = _relationshipKeys.Resolve(receiverBound);
        return [.. slotTargets.Where(target =>
            (bound.Unbounded || MayReach(target.ReceiverType, bound.Own)) &&
            _relationships.Classify(target.ReceiverType, receiverBound).IsAssignmentCompatible)];
    }

    private bool MayReach(CliTypeIdentity receiver, string key) =>
        _relationshipKeys.Resolve(receiver) is var keys && (keys.Unbounded || keys.Reachable.Contains(key));
}
