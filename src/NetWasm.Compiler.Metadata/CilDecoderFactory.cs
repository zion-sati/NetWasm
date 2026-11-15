// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICilDecoderFactory
{
    ICilDecoder Create();
}

internal sealed class CilDecoderFactory(
    IMetadataMethodBodyBlockReader methodBodies,
    ISymbolFormatter symbols,
    IMetadataMethodReferenceResolver methodReferences,
    IMetadataTypeEntityResolver typeEntities,
    IMetadataFieldReferenceResolver fieldReferences,
    IMetadataTypeSignatureResolver typeSignatures,
    IMetadataCallSiteSignatureResolver callSiteSignatures,
    IMetadataStackTypeResolver stackTypes,
    ITypedReferenceCallResolver typedReferences) : ICilDecoderFactory
{
    public ICilDecoder Create() =>
        new CilDecoder(
            methodBodies,
            symbols,
            methodReferences,
            typeEntities,
            fieldReferences,
            typeSignatures,
            callSiteSignatures,
            stackTypes,
            new CilSwitchLowerer(),
            new PlatformGuardPlanner(new RawCilInstructionReader()),
            new CilTryReentryLowerer(),
            typedReferences);
}
