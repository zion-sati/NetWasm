using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal interface IAssignableTypeMetadataBuilderFactory
{
    IAssignableTypeMetadataBuilder Create(
        MetadataCompilationSnapshot metadata,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state);
}

internal sealed class AssignableTypeMetadataBuilderFactory(
    IImplementedInterfaceResolverFactory implementedInterfaces,
    ITypeRelationshipClassifierFactory relationships) :
    IAssignableTypeMetadataBuilderFactory
{
    private readonly IImplementedInterfaceResolverFactory _implementedInterfaces =
        implementedInterfaces ?? throw new ArgumentNullException(nameof(implementedInterfaces));
    private readonly ITypeRelationshipClassifierFactory _relationships = relationships ??
        throw new ArgumentNullException(nameof(relationships));

    public IAssignableTypeMetadataBuilder Create(
        MetadataCompilationSnapshot metadata,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state)
    {
        var relationshipClassifier = _relationships.Create(
            typeFinder,
            typeDefinitions,
            identities,
            _implementedInterfaces.Create(metadata),
            new LayoutBaseTypeResolver(typeFinder, identities, identityBaseTypes));
        return new AssignableTypeMetadataBuilder(
            identities,
            typeDefinitions,
            relationshipClassifier,
            types,
            state);
    }
}

internal sealed class EmptyAssignableTypeMetadataBuilder : IAssignableTypeMetadataBuilder
{
    public static EmptyAssignableTypeMetadataBuilder Instance { get; } = new();

    private EmptyAssignableTypeMetadataBuilder()
    {
    }

    public AssignableTypeMetadataLayout Build(CliTypeIdentity candidate) => default;
}

internal sealed class EmptyAssignableTypeMetadataBuilderFactory :
    IAssignableTypeMetadataBuilderFactory
{
    public IAssignableTypeMetadataBuilder Create(
        MetadataCompilationSnapshot metadata,
        ITypeFinder typeFinder,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state) =>
        EmptyAssignableTypeMetadataBuilder.Instance;
}
