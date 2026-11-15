// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeEntityResolver(
    IMetadataSignatureTypeResolver signatures,
    ITypeDefinitionResolver types)
    : IMetadataTypeEntityResolver
{
    public EntityKey Resolve(MetadataAssemblySnapshot source, EntityHandle handle, CliGenericContext? genericContext = null) =>
        types.ResolveTypeIdentity(signatures.Resolve(source, handle, genericContext)).Key;
}
