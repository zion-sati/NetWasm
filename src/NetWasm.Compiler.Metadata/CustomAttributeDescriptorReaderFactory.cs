// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class CustomAttributeDescriptorReaderFactory : ICustomAttributeDescriptorReaderFactory
{
    public ICustomAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities) =>
        new CustomAttributeDescriptorReader(
            metadata.Assemblies.ToImmutableDictionary(assembly => assembly.Identity, assembly => assembly.Metadata),
            definitions,
            new MetadataSignatureTypeResolver(new MetadataTypeResolver(definitions), identities));
}

public sealed class AssemblyAttributeDescriptorReaderFactory : IAssemblyAttributeDescriptorReaderFactory
{
    public IAssemblyAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities) =>
        new AssemblyAttributeDescriptorReader(
            metadata.Assemblies.ToImmutableDictionary(assembly => assembly.Identity, assembly => assembly.Metadata),
            new MetadataSignatureTypeResolver(new MetadataTypeResolver(definitions), identities));
}
