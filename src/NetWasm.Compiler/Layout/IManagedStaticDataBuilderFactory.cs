// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal interface IManagedStaticDataBuilderFactory
{
    IManagedStaticDataBuilder Create(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ReachableProgram program,
        ManagedTypeLayouts types);
}
