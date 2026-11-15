// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataIdentityBaseTypeResolver(
    IBaseTypeIdentityResolver baseTypeIdentities) : IMetadataIdentityBaseTypeResolver
{
    public CliTypeIdentity? GetBaseType(CliTypeIdentity type)
    {
        var baseType = baseTypeIdentities.GetBaseTypeIdentity(type);
        return baseType;
    }
}
