// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeMatchSelector(
    ICustomAttributeDescriptorReader descriptors,
    IAttributeUsageReader usages,
    ITypeRelationshipClassifier relationships,
    IBaseTypeResolver bases) : IAttributeMatchSelector
{
    public ImmutableArray<CustomAttributeDescriptor> Select(CliTypeIdentity target, CliTypeIdentity filter,
        bool inherit, bool existenceOnly)
    {
        var selected = ImmutableArray.CreateBuilder<CustomAttributeDescriptor>();
        var derivedTypes = new HashSet<CliTypeIdentity>();
        for (var current = target; current is not null; current = inherit ? bases.Resolve(current) : null)
        {
            var level = ImmutableArray.CreateBuilder<CustomAttributeDescriptor>();
            foreach (var attribute in descriptors.Read(current))
            {
                if (!relationships.Classify(attribute.AttributeType, filter).IsAssignmentCompatible) continue;
                if (!current.Equals(target))
                {
                    var usage = usages.Read(attribute.AttributeType);
                    if (!usage.Inherited || !usage.AllowMultiple && derivedTypes.Contains(attribute.AttributeType)) continue;
                }
                if (existenceOnly) return [attribute];
                level.Add(attribute);
            }
            selected.AddRange(level);
            foreach (var attribute in level) derivedTypes.Add(attribute.AttributeType);
        }
        return selected.ToImmutable();
    }
}
