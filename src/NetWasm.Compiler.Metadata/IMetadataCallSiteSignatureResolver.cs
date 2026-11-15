// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataCallSiteSignatureResolver
{
    // The calli at offset in method (for diagnostics) reads the call site metadataToken names.
    CilOperand.CallSite Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext genericContext,
        string method,
        int offset);
}
