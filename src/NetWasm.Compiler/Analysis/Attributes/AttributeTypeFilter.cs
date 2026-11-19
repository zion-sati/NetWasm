// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeTypeFilter(
    ITypeRelationshipClassifier relationships,
    IBaseTypeResolver bases,
    ITypeDefinitionResolver definitions) : IAttributeTypeFilter
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly IBaseTypeResolver _bases = bases ?? throw new ArgumentNullException(nameof(bases));
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public bool Matches(CliTypeIdentity attributeType, CliTypeIdentity filter)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        ArgumentNullException.ThrowIfNull(filter);
        if (!IsOpenGenericDefinition(filter, _definitions))
        {
            return _relationships.Classify(attributeType, filter).IsAssignmentCompatible;
        }
        for (CliTypeIdentity? type = attributeType; type is not null; type = _bases.Resolve(type))
        {
            if (type.Shape == CliTypeShape.GenericInstantiation && filter.Equals(type.ElementType))
            {
                return true;
            }
        }
        return false;
    }

    // typeof(TagAttribute<>): the generic definition itself, with no type arguments.
    internal static bool IsOpenGenericDefinition(CliTypeIdentity type, ITypeDefinitionResolver definitions) =>
        type.Shape == CliTypeShape.Named && type.TypeArguments.IsEmpty &&
        definitions.ResolveTypeIdentity(type).GenericArity > 0;
}
