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
    IAttributeTypeFilter? types = null,
    IPseudoAttributeSelector? pseudoAttributes = null,
    IModuleAttributeDescriptorReader? moduleAttributes = null) : IAttributeQueryPlanner
{
    public AttributeQueryEntry Plan(ResolvedAttributeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var existence = query.Call.Operation == AttributeQueryOperation.IsDefined;
        return new(query.Target, query.Filter, Result(false), Result(true)) { TargetAssembly = query.TargetAssembly };

        AttributeQueryResult Result(bool inherit)
        {
            // An assembly has no base to inherit from: both results are its own attributes of the (sealed) filter type.
            var unreached = query.InheritConstant is { } constant && constant != inherit;
            var selected = unreached ? [] :
                query.TargetAssembly is { } assembly
                    ? Assembly(assembly)
                    : query.TargetModule is { } module
                        ? Module(module)
                        : matches.Select(query.Target!, query.Filter, inherit, existence);
            // Upstream puts a type's pseudo-attributes before the inherited walk's results, and after its own where it
            // reads no base (no inheritance, or a sealed filter that is not inherited).
            var pseudo = unreached || query.Target is not { } target || pseudoAttributes is null
                ? []
                : pseudoAttributes.Select(target, query.Filter);
            if (existence)
            {
                return new AttributeExistenceResult(!selected.IsEmpty || !pseudo.IsEmpty);
            }
            var own = selected.Select(constructions.Plan);
            return new AttributeRetrievalResult(inherit && !pseudo.IsEmpty && !query.Filter.Equals(pseudo[0].AttributeType)
                ? [.. pseudo, .. own]
                : [.. own, .. pseudo]);
        }

        // A module's attributes, each checked against the filter; like an assembly, a module has no base.
        ImmutableArray<CustomAttributeDescriptor> Module(AssemblyIdentity assembly) =>
            [.. (moduleAttributes ?? throw new InvalidOperationException("Module queries need the module attribute reader."))
                .Read(assembly)
                .Where(attribute => types is null || types.Matches(attribute.AttributeType, query.Filter))];

        // The reader finds attributes by the filter's name where the filter takes one sealed type, and those of a
        // generic attribute type (whose constructor belongs to a type specification, which has no name) whatever their
        // type; otherwise it reads them all. Each is checked against the filter.
        ImmutableArray<CustomAttributeDescriptor> Assembly(AssemblyIdentity assembly)
        {
            var reader = assemblyAttributes ?? throw new InvalidOperationException("Assembly queries need the assembly attribute reader.");
            return [.. reader.Read(assembly, query.AttributeName)
                .Where(attribute => types is null || types.Matches(attribute.AttributeType, query.Filter))];
        }
    }
}
