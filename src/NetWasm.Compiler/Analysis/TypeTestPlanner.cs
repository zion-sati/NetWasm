// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeTestPlanner(
    ITypeOperandResolver typeOperands,
    ITypeRelationshipClassifier relationships,
    ITypeRelationshipKeyResolver relationshipKeys,
    INullableTypeResolver nullableTypes) : ITypeTestPlanner
{
    public ImmutableDictionary<string, TypeTestSiteModel> Build(
        IEnumerable<ManagedMethodBody> methods,
        IEnumerable<CliTypeIdentity> allocatedTypes)
    {
        // Each site classifies only the allocated types filed under its target's key.
        var allocated = allocatedTypes.ToList();
        var byKey = new Dictionary<string, List<CliTypeIdentity>>(StringComparer.Ordinal);
        var unbounded = new List<CliTypeIdentity>();
        foreach (var allocatedType in allocated)
        {
            var keys = relationshipKeys.Resolve(allocatedType);
            if (keys.Unbounded)
            {
                unbounded.Add(allocatedType);
                continue;
            }
            foreach (var key in keys.Reachable)
            {
                if (!byKey.TryGetValue(key, out var filed))
                {
                    byKey.Add(key, filed = []);
                }
                filed.Add(allocatedType);
            }
        }
        IEnumerable<CliTypeIdentity> Candidates(CliTypeIdentity target) =>
            relationshipKeys.Resolve(target) is { Unbounded: false } keys
                ? (byKey.GetValueOrDefault(keys.Own) ?? []).Concat(unbounded)
                : allocated;
        // The allocated types a target admits depend on the target alone, and a program tests the same targets at
        // many sites (every cast to object or to a common interface), so each target's are classified once and its
        // sites share them.
        var matching = new Dictionary<CliTypeIdentity, ImmutableArray<CliTypeIdentity>>();
        ImmutableArray<CliTypeIdentity> Matching(CliTypeIdentity target)
        {
            if (!matching.TryGetValue(target, out var types))
            {
                matching.Add(target, types = [.. Candidates(target)
                    .Where(allocatedType => relationships
                        .Classify(allocatedType, target)
                        .IsAssignmentCompatible)
                    .OrderBy(allocatedType => allocatedType.CanonicalName, StringComparer.Ordinal)]);
            }
            return types;
        }
        return methods
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.Operation is
                    CilOperation.CastClass or
                    CilOperation.IsInstance or
                    CilOperation.UnboxAny)
                .Select(instruction =>
                {
                    var target = typeOperands.Resolve(instruction, method.Method);
                    var membershipTarget = nullableTypes.Resolve(target) ?? target;
                    return (
                        Method: method.Method,
                        Instruction: instruction,
                        Target: target,
                        MembershipTarget: membershipTarget);
                }))
            .Where(candidate =>
                candidate.Instruction.Operation != CilOperation.UnboxAny ||
                !candidate.Target.IsValueType)
            .Select(candidate => new TypeTestSiteModel(
                candidate.Method.CanonicalName,
                candidate.Instruction.Offset,
                candidate.Target,
                Matching(candidate.MembershipTarget)))
            .ToImmutableDictionary(site => site.Key, StringComparer.Ordinal);
    }
}
