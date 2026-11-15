// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeDescriptorReaderFactory
{
    ICustomAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities);
}

public interface IAssemblyAttributeDescriptorReaderFactory
{
    IAssemblyAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities);
}
