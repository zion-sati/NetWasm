using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationAnalysisBuilder(
    IWholeProgramAnalyzerFactory analyzers,
    IRuntimeIntrinsicRegistryFactory intrinsics,
    IReachabilityInvariantValidator reachabilityInvariants,
    IComponentReachabilityValidator componentReachability,
    IPlainCoreModuleValidator plainCoreModules,
    ITypeRepositoryFactory typeRepositories,
    IFieldRepositoryFactory fieldRepositories,
    IMethodRepositoryFactory methodRepositories,
    ITypeClassifierFactory classifiers,
    ITypeFinderFactory typeFinders,
    ITypeDefinitionResolverFactory typeDefinitions,
    ITypeIdentityResolverFactory identities,
    IBaseTypeIdentityResolverFactory baseTypeIdentities,
    IImplementedInterfaceResolverFactory implementedInterfaces,
    IMethodBodyReaderFactory bodies,
    IMethodInstanceResolverFactory methodInstances,
    IMethodImplementationResolverFactory methodImplementations,
    ISymbolFormatterFactory symbols,
    IJavaScriptAsyncBindingResolverFactory asyncBindings,
    IConstructorReceiverProverFactory constructorReceivers,
    IUnlinkedNativeImportReporter unlinkedNativeImports) : ICompilationAnalysisBuilder
{
    private readonly IWholeProgramAnalyzerFactory _analyzers = analyzers ??
        throw new ArgumentNullException(nameof(analyzers));
    private readonly IRuntimeIntrinsicRegistryFactory _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly IReachabilityInvariantValidator _reachabilityInvariants =
        reachabilityInvariants ?? throw new ArgumentNullException(nameof(reachabilityInvariants));
    private readonly IComponentReachabilityValidator _componentReachability =
        componentReachability ?? throw new ArgumentNullException(nameof(componentReachability));
    private readonly IPlainCoreModuleValidator _plainCoreModules =
        plainCoreModules ?? throw new ArgumentNullException(nameof(plainCoreModules));
    private readonly ITypeRepositoryFactory _typeRepositories = typeRepositories ??
        throw new ArgumentNullException(nameof(typeRepositories));
    private readonly IFieldRepositoryFactory _fieldRepositories = fieldRepositories ??
        throw new ArgumentNullException(nameof(fieldRepositories));
    private readonly IMethodRepositoryFactory _methodRepositories = methodRepositories ??
        throw new ArgumentNullException(nameof(methodRepositories));
    private readonly ITypeClassifierFactory _classifiers = classifiers ??
        throw new ArgumentNullException(nameof(classifiers));
    private readonly ITypeFinderFactory _typeFinders = typeFinders ??
        throw new ArgumentNullException(nameof(typeFinders));
    private readonly ITypeDefinitionResolverFactory _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolverFactory _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly IBaseTypeIdentityResolverFactory _baseTypeIdentities = baseTypeIdentities ??
        throw new ArgumentNullException(nameof(baseTypeIdentities));
    private readonly IImplementedInterfaceResolverFactory _implementedInterfaces =
        implementedInterfaces ?? throw new ArgumentNullException(nameof(implementedInterfaces));
    private readonly IMethodBodyReaderFactory _bodies = bodies ??
        throw new ArgumentNullException(nameof(bodies));
    private readonly IMethodInstanceResolverFactory _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly IMethodImplementationResolverFactory _methodImplementations =
        methodImplementations ?? throw new ArgumentNullException(nameof(methodImplementations));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly IJavaScriptAsyncBindingResolverFactory _asyncBindings = asyncBindings ??
        throw new ArgumentNullException(nameof(asyncBindings));
    private readonly IConstructorReceiverProverFactory _constructorReceivers = constructorReceivers ??
        throw new ArgumentNullException(nameof(constructorReceivers));
    private readonly IUnlinkedNativeImportReporter _unlinkedNativeImports = unlinkedNativeImports ??
        throw new ArgumentNullException(nameof(unlinkedNativeImports));

    public CompilationAnalysis Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(preparation);

        var types = _typeRepositories.Create(metadata);
        var fields = _fieldRepositories.Create(metadata);
        var methods = _methodRepositories.Create(metadata);
        var typeClassifier = _classifiers.Create(metadata);
        var typeFinder = _typeFinders.Create(metadata);
        var typeDefinitions = _typeDefinitions.Create(metadata);
        var identities = _identities.Create(metadata);
        var baseTypeIdentities = _baseTypeIdentities.Create(metadata);
        var implementedInterfaces = _implementedInterfaces.Create(metadata);
        var bodies = _bodies.Create(metadata);
        var methodInstances = _methodInstances.Create(metadata);
        var methodImplementations = _methodImplementations.Create(metadata);
        var symbols = _symbols.Create(metadata);
        var world = preparation.World;
        var asyncBindingResolver = _asyncBindings.Create(
            typeFinder,
            typeDefinitions,
            identities,
            fields,
            methods,
            methodInstances,
            symbols);

        var runtimeIntrinsics = _intrinsics.Create(symbols, metadata.Methods);
        var program = _analyzers.Create(
            metadata,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypeIdentities,
            implementedInterfaces,
            bodies,
            methodInstances,
            methodImplementations,
            symbols,
            asyncBindingResolver.Resolve,
            runtimeIntrinsics,
            preparation.OptimizationSettings,
            world).Analyze(
            preparation.Entry.EntryPoint,
            preparation.Exports,
            preparation.ReachabilityRoots);
        _reachabilityInvariants.Validate(
            methodInstances,
            typeClassifier,
            program,
            runtimeIntrinsics);
        // A plain core module's own rules first: a WASI import it reaches is not the world's.
        _plainCoreModules.Validate(program, preparation.Exports, world, methods, symbols);
        _componentReachability.Validate(
            program,
            preparation.Exports,
            preparation.ComponentContract,
            methods,
            symbols);
        _unlinkedNativeImports.Report(program, symbols);
        // Whether a constructor's this can be null depends on every call of it, so
        // its proof waits for the whole program.
        var proven = _constructorReceivers.Create(types, fields, methods, methodInstances, symbols,
            preparation.OptimizationSettings.NewObjectNullCheckElimination ==
            NewObjectNullCheckEliminationPolicy.ConstructedObjects).Prove(program);
        return new CompilationAnalysis(proven);
    }
}
