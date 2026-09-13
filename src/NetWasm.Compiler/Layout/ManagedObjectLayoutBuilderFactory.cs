using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedObjectLayoutBuilderFactory :
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
            valueLayouts);
}
