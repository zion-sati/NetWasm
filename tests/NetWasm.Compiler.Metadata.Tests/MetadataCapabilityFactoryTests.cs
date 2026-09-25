using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCapabilityFactoryTests
{
    [Fact]
    public void FactoriesMaterializeEachIndependentCapability()
    {
        using var assets = TestAssets.Create();
        using var lease = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var snapshot = lease.Snapshot;

        var materializations = new MetadataCompilationMaterializationFactory();
        var types = new MetadataTypeRepositoryFactory(materializations);
        var fields = new MetadataFieldRepositoryFactory(materializations);
        var typeFinder = new MetadataTypeFinderFactory(materializations);
        var definitions = new MetadataTypeDefinitionResolverFactory(materializations, typeFinder);
        var methods = new MetadataMethodRepositoryFactory(materializations, definitions);
        var identities = new MetadataTypeIdentityResolverFactory(types);
        var baseTypeIdentities = new MetadataBaseTypeIdentityResolverFactory(
            materializations,
            definitions,
            identities);
        var entityBaseTypes = new MetadataEntityBaseTypeResolverFactory(materializations, definitions);
        var identityBaseTypes = new MetadataIdentityBaseTypeResolverFactory(baseTypeIdentities);
        var classifier = new MetadataTypeClassifierFactory(
            identities,
            definitions,
            identityBaseTypes);
        var interfaces = new MetadataImplementedInterfaceResolverFactory(
            materializations,
            definitions,
            identities);
        var methodImplementations = new MetadataMethodImplementationResolverFactory(
            materializations,
            definitions,
            types,
            methods);
        var methodInstances = new MetadataMethodInstanceResolverFactory(
            materializations,
            definitions,
            types,
            methods);
        var symbols = new MetadataSymbolFormatterFactory(types);
        var methodBodies = new MetadataMethodBodyReaderFactory(
            materializations,
            definitions,
            types,
            fields,
            methods,
            symbols);
        var methodFinder = new MetadataMethodFinderFactory(materializations, methods);

        var materialized = materializations.Create(snapshot);
        Assert.NotEmpty(materialized.MetadataAssemblies);
        Assert.Same(materialized, materializations.Create(snapshot));
        Assert.NotNull(types.Create(snapshot));
        Assert.NotNull(fields.Create(snapshot));
        Assert.NotNull(methods.Create(snapshot));
        Assert.NotNull(typeFinder.Create(snapshot));
        Assert.NotNull(definitions.Create(snapshot));
        Assert.NotNull(identities.Create(snapshot));
        Assert.NotNull(baseTypeIdentities.Create(snapshot));
        Assert.NotNull(entityBaseTypes.Create(snapshot));
        Assert.NotNull(identityBaseTypes.Create(snapshot));
        Assert.NotNull(classifier.Create(snapshot));
        Assert.NotNull(interfaces.Create(snapshot));
        Assert.NotNull(methodImplementations.Create(snapshot));
        Assert.NotNull(methodInstances.Create(snapshot));
        Assert.NotNull(methodBodies.Create(snapshot));
        Assert.NotNull(methodFinder.Create(snapshot));
        Assert.NotNull(symbols.Create(snapshot));
    }

    [Fact]
    public void MaterializationUsesEmptyAliasesWhenSnapshotOmitsThem()
    {
        using var assets = TestAssets.Create();
        using var lease = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var snapshot = lease.Snapshot with { ReferenceAssemblyAliases = null };

        var materialization = new MetadataCompilationMaterializationFactory().Create(snapshot);

        Assert.Empty(materialization.ReferenceAssemblyAliases);
    }

    [Fact]
    public void FactoryConstructorsRejectMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeRepositoryFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataFieldRepositoryFactory(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new MetadataMethodRepositoryFactory(null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodRepositoryFactory(
            new MetadataCompilationMaterializationFactory(), null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeFinderFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeDefinitionResolverFactory(null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeDefinitionResolverFactory(
            new MetadataCompilationMaterializationFactory(), null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeIdentityResolverFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataSymbolFormatterFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodFinderFactory(null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodFinderFactory(
            new MetadataCompilationMaterializationFactory(), null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataBaseTypeIdentityResolverFactory(
            null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataBaseTypeIdentityResolverFactory(
            new MetadataCompilationMaterializationFactory(), null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataBaseTypeIdentityResolverFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataEntityBaseTypeResolverFactory(null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataEntityBaseTypeResolverFactory(
            new MetadataCompilationMaterializationFactory(), null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataIdentityBaseTypeResolverFactory(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeClassifierFactory(null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeClassifierFactory(
            new MetadataTypeIdentityResolverFactory(
                new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory())),
            null!,
            null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataTypeClassifierFactory(
            new MetadataTypeIdentityResolverFactory(
                new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory())),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataImplementedInterfaceResolverFactory(
            null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataImplementedInterfaceResolverFactory(
            new MetadataCompilationMaterializationFactory(), null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataImplementedInterfaceResolverFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodImplementationResolverFactory(
            null!, null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodImplementationResolverFactory(
            new MetadataCompilationMaterializationFactory(), null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodImplementationResolverFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodImplementationResolverFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodInstanceResolverFactory(
            null!, null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodInstanceResolverFactory(
            new MetadataCompilationMaterializationFactory(), null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodInstanceResolverFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodInstanceResolverFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodBodyReaderFactory(
            null!, null!, null!, null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodBodyReaderFactory(
            new MetadataCompilationMaterializationFactory(), null!, null!, null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodBodyReaderFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            null!, null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodBodyReaderFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            null!, null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodBodyReaderFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            new MetadataFieldRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            null!, null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodBodyReaderFactory(
            new MetadataCompilationMaterializationFactory(),
            new MetadataTypeDefinitionResolverFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeFinderFactory(new MetadataCompilationMaterializationFactory())),
            new MetadataTypeRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            new MetadataFieldRepositoryFactory(new MetadataCompilationMaterializationFactory()),
            new MetadataMethodRepositoryFactory(
                new MetadataCompilationMaterializationFactory(),
                new MetadataTypeDefinitionResolverFactory(
                    new MetadataCompilationMaterializationFactory(),
                    new MetadataTypeFinderFactory(
                        new MetadataCompilationMaterializationFactory()))),
            null!));
    }

    [Fact]
    public void FactoryMethodsRejectMissingSnapshots()
    {
        var materializations = new MetadataCompilationMaterializationFactory();
        var types = new MetadataTypeRepositoryFactory(materializations);
        var fields = new MetadataFieldRepositoryFactory(materializations);
        var finder = new MetadataTypeFinderFactory(materializations);
        var definitions = new MetadataTypeDefinitionResolverFactory(materializations, finder);
        var methods = new MetadataMethodRepositoryFactory(materializations, definitions);
        var identities = new MetadataTypeIdentityResolverFactory(types);
        var baseIdentities = new MetadataBaseTypeIdentityResolverFactory(
            materializations,
            definitions,
            identities);
        var identityBaseTypes = new MetadataIdentityBaseTypeResolverFactory(baseIdentities);
        var classifier = new MetadataTypeClassifierFactory(identities, definitions, identityBaseTypes);
        var implementations = new MetadataMethodImplementationResolverFactory(
            materializations, definitions, types, methods);
        var instances = new MetadataMethodInstanceResolverFactory(
            materializations, definitions, types, methods);
        var symbols = new MetadataSymbolFormatterFactory(types);
        var bodyReader = new MetadataMethodBodyReaderFactory(
            materializations,
            definitions,
            types,
            fields,
            methods,
            symbols);

        Assert.Throws<ArgumentNullException>(() => types.Create(null!));
        Assert.Throws<ArgumentNullException>(() => fields.Create(null!));
        Assert.Throws<ArgumentNullException>(() => methods.Create(null!));
        Assert.Throws<ArgumentNullException>(() => finder.Create(null!));
        Assert.Throws<ArgumentNullException>(() => definitions.Create(null!));
        Assert.Throws<ArgumentNullException>(() => identities.Create(null!));
        Assert.Throws<ArgumentNullException>(() => baseIdentities.Create(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataEntityBaseTypeResolverFactory(
            materializations, definitions).Create(null!));
        Assert.Throws<ArgumentNullException>(() => identityBaseTypes.Create(null!));
        Assert.Throws<ArgumentNullException>(() => classifier.Create(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataImplementedInterfaceResolverFactory(
            materializations, definitions, identities).Create(null!));
        Assert.Throws<ArgumentNullException>(() => implementations.Create(null!));
        Assert.Throws<ArgumentNullException>(() => instances.Create(null!));
        Assert.Throws<ArgumentNullException>(() => bodyReader.Create(null!));
        Assert.Throws<ArgumentNullException>(() => new MetadataMethodFinderFactory(
            materializations, methods).Create(null!));
        Assert.Throws<ArgumentNullException>(() => symbols.Create(null!));
    }
}
