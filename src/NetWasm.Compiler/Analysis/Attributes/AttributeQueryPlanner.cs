// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeQueryPlanner(IAttributeMatchSelector matches,
    IAttributeConstructionPlanner constructions,
    IAssemblyAttributeDescriptorReader? assemblyAttributes = null,
    IAttributeTypeFilter? types = null) : IAttributeQueryPlanner
{
    public AttributeQueryEntry Plan(ResolvedAttributeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var existence = query.Call.Operation == AttributeQueryOperation.IsDefined;
        return new(query.Target, query.Filter, Result(false), Result(true)) { TargetAssembly = query.TargetAssembly };

        AttributeQueryResult Result(bool inherit)
        {
            // An assembly has no base to inherit from: both results are its own attributes of the (sealed) filter type.
            var selected = query.InheritConstant is { } constant && constant != inherit ? [] :
                query.TargetAssembly is { } assembly
                    ? Assembly(assembly)
                    : matches.Select(query.Target!, query.Filter, inherit, existence);
            return existence ? new AttributeExistenceResult(!selected.IsEmpty) :
                new AttributeRetrievalResult([.. selected.Select(constructions.Plan)]);
        }

        // The reader finds attributes by the filter's name, and those of a generic attribute type (whose constructor
        // belongs to a type specification, which has no name) whatever their type, so each is checked against the
        // filter.
        ImmutableArray<CustomAttributeDescriptor> Assembly(AssemblyIdentity assembly) =>
            [.. (assemblyAttributes ?? throw new InvalidOperationException("Assembly queries need the assembly attribute reader."))
                // An assembly query is lowered only for a named filter or an open generic definition, which have names.
                .Read(assembly, query.Filter.FullName!)
                .Where(attribute => types is null || types.Matches(attribute.AttributeType, query.Filter))];
    }
}
