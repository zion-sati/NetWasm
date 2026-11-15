// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedObjectLayoutBuilderFactory(IExplicitValueLayoutResolver explicitLayouts) :
    IManagedObjectLayoutBuilderFactory
{
    public IManagedObjectLayoutBuilder Create(
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        WasmTargetLayout target,
        ManagedTypeLayoutBuildState state,
        IValueLayoutResolver valueLayouts) => new ManagedObjectLayoutBuilder(
            typeRepository,
            fields,
            types,
            typeDefinitions,
            identities,
            entityBaseTypes,
            identityBaseTypes,
            target,
            state,
            valueLayouts,
            explicitLayouts,
            new FieldLayoutFailureBinder());
}
