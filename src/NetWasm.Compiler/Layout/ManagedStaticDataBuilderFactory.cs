using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedStaticDataBuilderFactory(
    IExceptionTypeNameResolver exceptionTypeNames,
    IAssignableTypeMetadataBuilderFactory assignableTypeMetadata,
    IAssemblyIdentityFormatterFactory assemblyIdentityFormatters) :
    IManagedStaticDataBuilderFactory
{
    private readonly IExceptionTypeNameResolver _exceptionTypeNames = exceptionTypeNames ??
        throw new System.ArgumentNullException(nameof(exceptionTypeNames));
    private readonly IAssignableTypeMetadataBuilderFactory _assignableTypeMetadata =
        assignableTypeMetadata ?? throw new ArgumentNullException(nameof(assignableTypeMetadata));
    private readonly IAssemblyIdentityFormatterFactory _assemblyIdentityFormatters =
        assemblyIdentityFormatters ??
        throw new ArgumentNullException(nameof(assemblyIdentityFormatters));

    internal ManagedStaticDataBuilderFactory(IExceptionTypeNameResolver exceptionTypeNames) :
        this(
            exceptionTypeNames,
            new EmptyAssignableTypeMetadataBuilderFactory(),
            new AssemblyIdentityFormatterFactory())
    {
    }

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
        var assignableTypes = _assignableTypeMetadata.Create(
            metadata,
            typeFinder,
            typeDefinitions,
            identities,
            identityBaseTypes,
            types,
            state);
        var assemblyNames = _assemblyIdentityFormatters.Create(metadata.Assemblies);
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
                types.Target,
                state,
                bitmaps,
                assignableTypes,
                assemblyNames),
            new ConstructedTypeDescriptorBuilder(
                typeFinder,
                typeDefinitions,
                identityBaseTypes,
                objectLayouts,
                types,
                types.Target,
                state,
                bitmaps,
                assignableTypes,
                program.RequiresTypeFacts,
                program.TypeNamePayload,
                assemblyNames,
                program.DelegateInvokeDescriptors,
                program.Finalizers),
            new ValueTypeDescriptorBuilder(
                typeRepository,
                identities,
                types,
                types.Target,
                state,
                bitmaps),
            new RuntimeGenericArgumentMetadataBuilder(
                program,
                state),
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
                program,
                types,
                types.Target,
                state,
                _exceptionTypeNames),
            new EnumMetadataCollector(
                typeDefinitions, identities, types, program.EnumMetadataRequirements, state),
            new EnumMetadataBuilder(state, types.Target));
    }
}
