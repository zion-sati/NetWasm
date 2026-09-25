using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

internal static class MetadataCompilationTestActors
{
    internal static ITypeRepository Types(MetadataCompilationSnapshot snapshot) =>
        new MetadataTypeRepositoryFactory(Materializations()).Create(snapshot);

    internal static IFieldRepository Fields(MetadataCompilationSnapshot snapshot) =>
        new MetadataFieldRepositoryFactory(Materializations()).Create(snapshot);

    internal static IMethodRepository Methods(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        return new MetadataMethodRepositoryFactory(
            materializations,
            Definitions(materializations)).Create(snapshot);
    }

    internal static ITypeFinder TypeFinder(MetadataCompilationSnapshot snapshot) =>
        new MetadataTypeFinderFactory(Materializations()).Create(snapshot);

    internal static ITypeDefinitionResolver TypeDefinitions(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        return new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations)).Create(snapshot);
    }

    internal static ITypeIdentityResolver TypeIdentities(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        return new MetadataTypeIdentityResolverFactory(
            new MetadataTypeRepositoryFactory(materializations)).Create(snapshot);
    }

    internal static IBaseTypeIdentityResolver BaseTypeIdentities(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        return new MetadataBaseTypeIdentityResolverFactory(
            materializations,
            definitions,
            new MetadataTypeIdentityResolverFactory(
                new MetadataTypeRepositoryFactory(materializations))).Create(snapshot);
    }

    internal static IMetadataEntityBaseTypeResolver EntityBaseTypes(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        return new MetadataEntityBaseTypeResolverFactory(
            materializations,
            new MetadataTypeDefinitionResolverFactory(
                materializations,
                new MetadataTypeFinderFactory(materializations))).Create(snapshot);
    }

    internal static IMetadataIdentityBaseTypeResolver IdentityBaseTypes(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        var identities = new MetadataTypeIdentityResolverFactory(
            new MetadataTypeRepositoryFactory(materializations));
        var baseIdentities = new MetadataBaseTypeIdentityResolverFactory(
            materializations,
            definitions,
            identities);
        return new MetadataIdentityBaseTypeResolverFactory(baseIdentities).Create(snapshot);
    }

    internal static ITypeClassifier TypeClassifier(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        return new MetadataTypeClassifierFactory(
            new MetadataTypeIdentityResolverFactory(
                new MetadataTypeRepositoryFactory(materializations)),
            definitions,
            new MetadataIdentityBaseTypeResolverFactory(
                new MetadataBaseTypeIdentityResolverFactory(
                    materializations,
                    definitions,
                    new MetadataTypeIdentityResolverFactory(
                        new MetadataTypeRepositoryFactory(materializations))))).Create(snapshot);
    }

    internal static IImplementedInterfaceResolver ImplementedInterfaces(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        return new MetadataImplementedInterfaceResolverFactory(
            materializations,
            definitions,
            new MetadataTypeIdentityResolverFactory(
                new MetadataTypeRepositoryFactory(materializations))).Create(snapshot);
    }

    internal static IMethodImplementationResolver MethodImplementations(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        var types = new MetadataTypeRepositoryFactory(materializations);
        return new MetadataMethodImplementationResolverFactory(
            materializations,
            definitions,
            types,
            new MetadataMethodRepositoryFactory(materializations, definitions)).Create(snapshot);
    }

    internal static IMethodInstanceResolver MethodInstances(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        var types = new MetadataTypeRepositoryFactory(materializations);
        return new MetadataMethodInstanceResolverFactory(
            materializations,
            definitions,
            types,
            new MetadataMethodRepositoryFactory(materializations, definitions)).Create(snapshot);
    }

    internal static IMethodBodyReader MethodBodies(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var types = new MetadataTypeRepositoryFactory(materializations);
        var definitions = new MetadataTypeDefinitionResolverFactory(
            materializations,
            new MetadataTypeFinderFactory(materializations));
        var symbols = new MetadataSymbolFormatterFactory(types);
        return new MetadataMethodBodyReaderFactory(
            materializations,
            definitions,
            types,
            new MetadataFieldRepositoryFactory(materializations),
            new MetadataMethodRepositoryFactory(materializations, definitions),
            symbols).Create(snapshot);
    }

    internal static IMethodFinder MethodFinder(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var definitions = Definitions(materializations);
        return new MetadataMethodFinderFactory(
            materializations,
            new MetadataMethodRepositoryFactory(materializations, definitions)).Create(snapshot);
    }

    internal static ISymbolFormatter Symbols(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        return new MetadataSymbolFormatterFactory(
            new MetadataTypeRepositoryFactory(materializations)).Create(snapshot);
    }

    private static MetadataCompilationMaterializationFactory Materializations() =>
        new();

    private static MetadataTypeDefinitionResolverFactory Definitions(
        IMetadataCompilationMaterializationFactory materializations) => new(
        materializations,
        new MetadataTypeFinderFactory(materializations));
}
