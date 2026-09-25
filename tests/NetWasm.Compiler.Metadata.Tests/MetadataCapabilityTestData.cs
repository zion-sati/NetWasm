using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

internal static class MetadataCapabilityTestData
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

    internal static IMethodInstanceResolver MethodInstances(
        MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var types = new MetadataTypeRepositoryFactory(materializations);
        var definitions = Definitions(materializations);
        var methods = new MetadataMethodRepositoryFactory(materializations, definitions);
        return new MetadataMethodInstanceResolverFactory(
            materializations,
            definitions,
            types,
            methods).Create(snapshot);
    }

    internal static IMethodBodyReader MethodBodies(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var types = new MetadataTypeRepositoryFactory(materializations);
        var fields = new MetadataFieldRepositoryFactory(materializations);
        var definitions = Definitions(materializations);
        var methods = new MetadataMethodRepositoryFactory(materializations, definitions);
        var symbols = new MetadataSymbolFormatterFactory(types);
        return new MetadataMethodBodyReaderFactory(
            materializations,
            definitions,
            types,
            fields,
            methods,
            symbols).Create(snapshot);
    }

    internal static IMethodFinder MethodFinder(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        var methods = new MetadataMethodRepositoryFactory(
            materializations,
            Definitions(materializations));
        return new MetadataMethodFinderFactory(materializations, methods).Create(snapshot);
    }

    internal static ISymbolFormatter Symbols(MetadataCompilationSnapshot snapshot)
    {
        var materializations = Materializations();
        return new MetadataSymbolFormatterFactory(
            new MetadataTypeRepositoryFactory(materializations)).Create(snapshot);
    }

    private static MetadataCompilationMaterializationFactory Materializations() =>
        new MetadataCompilationMaterializationFactory();

    private static MetadataTypeDefinitionResolverFactory Definitions(
        IMetadataCompilationMaterializationFactory materializations) => new(
        materializations,
        new MetadataTypeFinderFactory(materializations));
}
