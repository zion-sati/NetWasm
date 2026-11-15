// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

/// <summary>Formats a type definition's name as ILLink.Substitutions.xml names it.</summary>
internal interface ITypeDefinitionNameFormatter
{
    string Format(MetadataReader reader, TypeDefinitionHandle type);
}
