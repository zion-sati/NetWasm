// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataSignatureTypeResolver
{
    CliTypeIdentity Resolve(
        MetadataAssemblySnapshot source,
        EntityHandle handle,
        CliGenericContext? genericContext = null);
}
