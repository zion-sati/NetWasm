using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedLayoutCompiler(
    IManagedTypeLayoutCompilerFactory typeLayoutCompilers,
    IManagedStaticDataBuilderFactory staticDataBuilders) : IManagedLayoutCompiler
{
    private readonly IManagedTypeLayoutCompilerFactory _typeLayoutCompilers =
        typeLayoutCompilers ?? throw new ArgumentNullException(nameof(typeLayoutCompilers));
    private readonly IManagedStaticDataBuilderFactory _staticDataBuilders =
        staticDataBuilders ?? throw new ArgumentNullException(nameof(staticDataBuilders));

    public ManagedLayoutSnapshot Compile(
        MetadataCompilationSnapshot metadata,
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ReachableProgram program,
        WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(typeRepository);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(typeFinder);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(entityBaseTypes);
        ArgumentNullException.ThrowIfNull(identityBaseTypes);
        ArgumentNullException.ThrowIfNull(program);
        var targetLayout = WasmTargetLayout.For(target);
        var typeLayouts = _typeLayoutCompilers.Create(
            metadata,
            typeRepository,
            fields,
            typeFinder,
            typeDefinitions,
            identities,
            entityBaseTypes,
            identityBaseTypes,
            program,
            targetLayout).Compile();
        var staticData = _staticDataBuilders.Create(
            metadata,
            typeRepository,
            fields,
            typeFinder,
            typeDefinitions,
            identities,
            identityBaseTypes,
            program,
            typeLayouts).Build();
        return new ManagedLayoutSnapshot(typeLayouts, staticData);
    }
}
