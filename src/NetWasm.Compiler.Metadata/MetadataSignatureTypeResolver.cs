// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataSignatureTypeResolver(
    IMetadataTypeResolver types,
    ITypeIdentityResolver identities) : IMetadataSignatureTypeResolver
{
    public CliTypeIdentity Resolve(
        MetadataAssemblySnapshot source,
        EntityHandle handle,
        CliGenericContext? genericContext = null)
    {
        if (handle.Kind == HandleKind.TypeSpecification)
        {
            return source.Reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(
                    new SignatureTypeProvider(
                        source.Identity,
                        source.Reader,
                        source.AssemblyIdentityAliases),
                    genericContext ?? CliGenericContext.Empty);
        }
        return identities.GetTypeIdentity(types.Resolve(source, handle).Key);
    }
}
