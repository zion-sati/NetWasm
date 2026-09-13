using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedTypeLayoutCompilerFactory(
    IValueLayoutResolverFactory valueLayoutResolvers,
    IManagedObjectLayoutBuilderFactory objectLayoutBuilders,
    IExceptionTypeNameResolver exceptionTypeNames) :
    IManagedTypeLayoutCompilerFactory
{
    private readonly IValueLayoutResolverFactory _valueLayoutResolvers =
        valueLayoutResolvers ?? throw new ArgumentNullException(nameof(valueLayoutResolvers));
    private readonly IManagedObjectLayoutBuilderFactory _objectLayoutBuilders =
        objectLayoutBuilders ?? throw new ArgumentNullException(nameof(objectLayoutBuilders));
    private readonly IExceptionTypeNameResolver _exceptionTypeNames = exceptionTypeNames ??
        throw new ArgumentNullException(nameof(exceptionTypeNames));

    public IManagedTypeLayoutCompiler Create(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ReachableProgram program,
        WasmTargetLayout target)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(typeRepository);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(typeFinder);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(entityBaseTypes);
        ArgumentNullException.ThrowIfNull(identityBaseTypes);
        var state = new ManagedValueLayoutState();
        var resolver = _valueLayoutResolvers.Create(
            typeDefinitions,
            fields,
            target,
            state);
        var buildState = new ManagedTypeLayoutBuildState(state);
        var objectLayouts = _objectLayoutBuilders.Create(
            typeRepository,
            fields,
            typeFinder,
            typeDefinitions,
            identities,
            entityBaseTypes,
            identityBaseTypes,
            target,
            buildState,
            resolver);
        var reachableObjects = new ReachableObjectLayoutBuilder(
            typeRepository,
            program,
            objectLayouts);
        var staticFieldValues = new StaticFieldValueLayoutResolver(
            fields,
            program,
            resolver);
        var methodValues = new MethodValueLayoutResolver(program, resolver);
        var implicitObjects = new ImplicitObjectLayoutBuilder(
            typeFinder,
            typeRepository,
            program,
            objectLayouts,
            _exceptionTypeNames,
            buildState);
        var valueTypes = new ValueTypeLayoutResolver(
            typeRepository,
            fields,
            identities,
            resolver,
            buildState,
            metadata.Types);
        var delegateOffsets = new DelegateFieldOffsetResolver(
            typeFinder,
            fields,
            buildState);
        return new ManagedTypeLayoutCompiler(
            buildState,
            target,
            reachableObjects,
            staticFieldValues,
            methodValues,
            implicitObjects,
            valueTypes,
            delegateOffsets);
    }
}
