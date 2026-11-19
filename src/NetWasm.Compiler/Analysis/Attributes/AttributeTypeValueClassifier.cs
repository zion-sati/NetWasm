// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// A parameter, local, field or return typed System.Type, or as a class derived from it, holds a Type (or null).
internal sealed class AttributeTypeValueClassifier(ITypeRelationshipClassifier relationships, CliTypeIdentity typeType) :
    IAttributeTypeValueClassifier
{
    private readonly ITypeRelationshipClassifier _relationships =
        relationships ?? throw new ArgumentNullException(nameof(relationships));
    private readonly CliTypeIdentity _typeType = typeType ?? throw new ArgumentNullException(nameof(typeType));

    public bool IsTypeValue(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape == CliTypeShape.Named &&
            (type.Equals(_typeType) || _relationships.Classify(type, _typeType).IsAssignmentCompatible);
    }
}
