// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataSymbolFormatter(
    ITypeRepository types) : ISymbolFormatter
{
    public string Format(EntityKey key) => types.GetTypeDefinition(key).FullName;

    public string Format(MethodDefinitionModel method) =>
        $"{Format(method.DeclaringType)}::{method.Name}";
}
