// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

public interface IManagedLayoutCompiler
{
    ManagedLayoutSnapshot Compile(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ReachableProgram program,
        WasmTarget target);
}
