using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal interface IManagedObjectLayoutBuilderFactory
{
    IManagedObjectLayoutBuilder Create(
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        WasmTargetLayout target,
        ManagedTypeLayoutBuildState state,
        IValueLayoutResolver valueLayouts);
}
