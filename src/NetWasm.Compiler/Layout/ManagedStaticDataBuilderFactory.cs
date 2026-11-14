using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedStaticDataBuilderFactory(
    IExceptionTypeNameResolver exceptionTypeNames,
    IAssignableTypeMetadataBuilderFactory assignableTypeMetadata,
    IImplementedInterfaceResolverFactory implementedInterfaces) :
    IManagedStaticDataBuilderFactory
{
    private readonly IImplementedInterfaceResolverFactory _implementedInterfaces = implementedInterfaces ??
        throw new ArgumentNullException(nameof(implementedInterfaces));
    private readonly IExceptionTypeNameResolver _exceptionTypeNames = exceptionTypeNames ??
        throw new System.ArgumentNullException(nameof(exceptionTypeNames));
    private readonly IAssignableTypeMetadataBuilderFactory _assignableTypeMetadata =
        assignableTypeMetadata ?? throw new ArgumentNullException(nameof(assignableTypeMetadata));

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
        var objectLayouts = new ObjectLayoutResolver(typeDefinitions, types);
        var assignableTypes = _assignableTypeMetadata.Create(
            metadata,
            typeFinder,
            typeDefinitions,
            identities,
            identityBaseTypes,
            types);
        return new ManagedStaticDataBuilder(
            types,
            state,
            new StaticFieldStorageBuilder(typeRepository, fields, program, types, state),
            new TypeDescriptorBuilder(
                identities,
                typeDefinitions,
                identityBaseTypes,
                objectLayouts,
                program,
                types,
                state,
                assignableTypes),
            new ConstructedTypeDescriptorBuilder(
                typeFinder,
                typeDefinitions,
                identityBaseTypes,
                objectLayouts,
                types,
                state,
                assignableTypes,
                program.Finalizers),
            new ValueTypeDescriptorBuilder(
                typeRepository,
                identities,
                types,
                types.Target,
                state),
            new RuntimeGenericArgumentMetadataBuilder(
                program,
                state),
            new RuntimeTypeTableBuilder(program, types.Target, state, identities,
                new RuntimeTypeAssemblyResolver(typeDefinitions), _implementedInterfaces.Create(metadata),
                new RuntimeTypeTableEncoder()),
            new MemberDescriptorDataBuilder(
                typeFinder,
                typeDefinitions,
                fields,
                program,
                types,
                types.Target,
                state),
            new StringDataBuilder(
                typeFinder,
                program,
                types,
                types.Target,
                state),
            new ExceptionObjectBuilder(
                typeFinder,
                fields,
                program,
                types,
                types.Target,
                state,
                _exceptionTypeNames),
            new TypeObjectDataBuilder(
                typeFinder,
                program,
                types,
                objectLayouts,
                new TypeOperandResolver(identities),
                types.Target,
                state),
            new EnumMetadataCollector(
                typeDefinitions, identities, types, program.EnumMetadataRequirements, state),
            new EnumMetadataBuilder(state, types.Target),
            new StructMarshallingDataCollector(typeDefinitions, fields, metadata.Methods, program, types, state),
            new StructMarshallingDataBuilder(program, types, state),
            program.UninitializedObjectTypes);
    }
}
