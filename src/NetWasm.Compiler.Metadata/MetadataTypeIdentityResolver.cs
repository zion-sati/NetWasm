// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeIdentityResolver(
    ITypeRepository types) : ITypeIdentityResolver
{
    public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
        CliTypeIdentity.FromDefinition(types.GetTypeDefinition(type));
}
