// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

/// <summary>Formats a method's signature as ILLink.Substitutions.xml names it.</summary>
internal interface IMethodSignatureFormatter
{
    string Format(MetadataReader reader, MethodDefinition method);
}
