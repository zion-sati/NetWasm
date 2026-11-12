using System;
using System.Collections.Immutable;
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
        ManagedTypeLayouts types);
}

internal sealed class AssignableTypeMetadataBuilderFactory(
    IImplementedInterfaceResolverFactory implementedInterfaces,
    ITypeRelationshipClassifierFactory relationships,
    ITypeRelationshipKeyResolverFactory relationshipKeys) :
    IAssignableTypeMetadataBuilderFactory
{
    private readonly ITypeRelationshipKeyResolverFactory _relationshipKeys = relationshipKeys ??
        throw new ArgumentNullException(nameof(relationshipKeys));
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
        ManagedTypeLayouts types)
    {
        var interfaces = _implementedInterfaces.Create(metadata);
        var baseTypes = new LayoutBaseTypeResolver(typeFinder, identities, identityBaseTypes);
        var relationshipClassifier = _relationships.Create(
            typeFinder,
            typeDefinitions,
            identities,
            interfaces,
            baseTypes);
        return new AssignableTypeMetadataBuilder(
            identities,
            typeDefinitions,
            relationshipClassifier,
            _relationshipKeys.Create(typeFinder, typeDefinitions, identities, interfaces, baseTypes),
            types);
    }
}

internal sealed class EmptyAssignableTypeMetadataBuilder : IAssignableTypeMetadataBuilder
{
    public static EmptyAssignableTypeMetadataBuilder Instance { get; } = new();

    private EmptyAssignableTypeMetadataBuilder()
    {
    }

    public ImmutableArray<int> Build(CliTypeIdentity candidate) => [];
}

// Null object: a type implements no interfaces, for layouts built without metadata relationships.
internal sealed class EmptyImplementedInterfaceResolverFactory : IImplementedInterfaceResolverFactory
{
    public IImplementedInterfaceResolver Create(MetadataCompilationSnapshot snapshot) => new Resolver();

    private sealed class Resolver : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) => [];
    }
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
        ManagedTypeLayouts types) =>
        EmptyAssignableTypeMetadataBuilder.Instance;
}
