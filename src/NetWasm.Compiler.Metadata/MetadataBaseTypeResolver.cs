// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataBaseTypeResolver(
    IMetadataAssemblyResolver assemblies,
    IMetadataTypeResolver types) : IMetadataEntityBaseTypeResolver
{
    public EntityKey? GetBaseType(EntityKey typeKey)
    {
        var source = assemblies.Resolve(typeKey.Assembly);
        var handle = source.BaseTypes[typeKey.MetadataToken];
        return handle.IsNil ? null : types.Resolve(source, handle).Key;
    }
}
