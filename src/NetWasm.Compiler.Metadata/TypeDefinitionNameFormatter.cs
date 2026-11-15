// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

// Adapts a type definition to the name a substitution's type element gives it ("Example.Outer/Inner"), named by the
// same Strategy that names a method signature's types.
internal sealed class TypeDefinitionNameFormatter(ISignatureTypeProvider<string, object?> types) : ITypeDefinitionNameFormatter
{
    private readonly ISignatureTypeProvider<string, object?> _types = types ?? throw new ArgumentNullException(nameof(types));

    public string Format(MetadataReader reader, TypeDefinitionHandle type)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _types.GetTypeFromDefinition(reader, type, 0);
    }
}
