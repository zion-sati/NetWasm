// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

// As upstream's PseudoCustomAttribute.GetCustomAttributes for a type: SerializableAttribute where the type is
// Serializable, ComImportAttribute where it is Import, for a filter of object, Attribute or that attribute type itself.
// An assembly and a module have none.
internal sealed class PseudoAttributeSelector(
    ITypeDefinitionResolver definitions,
    CliTypeIdentity objectType,
    CliTypeIdentity attributeType,
    AttributeConstructionPlan? serializable,
    AttributeConstructionPlan? comImport) : IPseudoAttributeSelector
{
    private const int Serializable = 0x2000;
    private const int Import = 0x1000;

    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public ImmutableArray<AttributeConstructionPlan> Select(CliTypeIdentity target, CliTypeIdentity filter)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(filter);
        var all = filter.Equals(objectType) || filter.Equals(attributeType);
        if (!all && !filter.Equals(serializable?.AttributeType) && !filter.Equals(comImport?.AttributeType) ||
            target.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
        {
            return [];
        }
        var flags = _definitions.ResolveTypeIdentity(target).Attributes;
        var selected = ImmutableArray.CreateBuilder<AttributeConstructionPlan>();
        if (serializable is not null && (all || filter.Equals(serializable.AttributeType)) && (flags & Serializable) != 0)
        {
            selected.Add(serializable);
        }
        if (comImport is not null && (all || filter.Equals(comImport.AttributeType)) && (flags & Import) != 0)
        {
            selected.Add(comImport);
        }
        return selected.ToImmutable();
    }
}
