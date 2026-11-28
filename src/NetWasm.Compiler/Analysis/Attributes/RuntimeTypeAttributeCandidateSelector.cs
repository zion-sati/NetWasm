// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: a run-time type is a case when the planner finds an attribute of the filter on it (or, where the query may
// inherit, on a base it inherits from). Only types the program already has at run time are candidates, so the cases
// root no type; each type's plan is kept, as the reducing closure asks again after every round.
internal sealed class RuntimeTypeAttributeCandidateSelector(IAttributeQueryPlanner planner) :
    IRuntimeTypeAttributeCandidateSelector
{
    private readonly IAttributeQueryPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly ConcurrentDictionary<(CliTypeIdentity Type, CliTypeIdentity Filter, AttributeQueryOperation Operation,
        bool? Inherit), (AttributeQueryEntry? Entry, string? Failure)> _plans = new();

    public RuntimeTypeAttributeSelection Select(ResolvedAttributeQuery query, IEnumerable<CliTypeIdentity> runtimeTypes)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(runtimeTypes);
        if (query.Target is not null || query.TargetAssembly is not null || query.TargetModule is not null)
        {
            throw new ArgumentException("A run-time Type query has no compile-time target.", nameof(query));
        }
        var cases = ImmutableArray.CreateBuilder<RuntimeTypeAttributeCase>();
        foreach (var type in runtimeTypes.Where(Candidate).Distinct().OrderBy(type => type.CanonicalName, StringComparer.Ordinal))
        {
            var key = (type, query.Filter, query.Call.Operation, query.InheritConstant);
            if (!_plans.TryGetValue(key, out var plan))
            {
                try
                {
                    plan = (_planner.Plan(query with { Target = type }), null);
                }
                catch (CompilerException exception)
                {
                    // A type whose attribute cannot be built here: the query keeps its run-time implementation, and the
                    // build says why (NW1019).
                    plan = (null, exception.Diagnostic.Message);
                }
                _plans[key] = plan;
            }
            if (plan.Failure is { } failure)
            {
                return RuntimeTypeAttributeSelection.Unplannable(failure);
            }
            if (plan.Entry is { } selection && Answers(selection, query.InheritConstant))
            {
                cases.Add(new(type, selection));
            }
        }
        return new(cases.ToImmutable(), null);
    }

    // Types that can carry custom attributes: closed classes, structs, interfaces, enums and primitives.
    private static bool Candidate(CliTypeIdentity type) => !type.ContainsGenericParameters &&
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive;

    private static bool Answers(AttributeQueryEntry selection, bool? inherit) => inherit switch
    {
        true => Found(selection.Inherited),
        false => Found(selection.Direct),
        null => Found(selection.Direct) || Found(selection.Inherited),
    };

    private static bool Found(AttributeQueryResult result) => result is AttributeExistenceResult presence
        ? presence.IsDefined
        : !((AttributeRetrievalResult)result).Attributes.IsEmpty;
}
