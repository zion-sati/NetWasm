// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataStackTypeResolver
{
    CliTypeIdentity Resolve(CliTypeIdentity type);

    CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext);
    MethodSignatureModel Resolve(MethodSignatureModel signature);

    MethodSignatureModel Resolve(MethodSignatureModel signature, CliGenericContext genericContext);
}
