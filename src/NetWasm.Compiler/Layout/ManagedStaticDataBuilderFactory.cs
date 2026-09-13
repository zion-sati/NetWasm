using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedStaticDataBuilderFactory(
    IExceptionTypeNameResolver exceptionTypeNames) : IManagedStaticDataBuilderFactory
{
    private readonly IExceptionTypeNameResolver _exceptionTypeNames = exceptionTypeNames ??
        throw new System.ArgumentNullException(nameof(exceptionTypeNames));

    public IManagedStaticDataBuilder Create(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ReachableProgram program,
        ManagedTypeLayouts types)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var state = new ManagedStaticDataBuildState();
        var bitmaps = new StaticReferenceBitmapBuilder();
        var objectLayouts = new ObjectLayoutResolver(typeDefinitions, types);
        return new ManagedStaticDataBuilder(
            types,
            state,
            new StaticFieldStorageBuilder(typeRepository, fields, program, types, state),
            new TypeDescriptorBuilder(
                identities,
                identityBaseTypes,
                objectLayouts,
                program,
                types,
                types.Target,
                state,
                bitmaps),
            new ConstructedTypeDescriptorBuilder(
                typeFinder,
                typeDefinitions,
                identityBaseTypes,
                objectLayouts,
                types,
                types.Target,
                state,
                bitmaps),
            new ValueTypeDescriptorBuilder(
                typeRepository,
                identities,
                types,
                types.Target,
                state,
                bitmaps),
            new StringDataBuilder(
                typeFinder,
                program,
                types,
                types.Target,
                state),
            new ExceptionObjectBuilder(
                typeFinder,
                program,
                types,
                types.Target,
                state,
                _exceptionTypeNames),
            new EnumMetadataCollector(typeRepository, types, state),
            new EnumMetadataBuilder(state, types.Target));
    }
}
