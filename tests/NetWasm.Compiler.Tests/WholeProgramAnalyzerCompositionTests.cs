using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Interop;
using System.Collections.Immutable;

using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Analysis.Attributes;

using NetWasm.Compiler.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Tests;

public sealed class WholeProgramAnalyzerCompositionTests
{
    [Fact]
    public void AllocationCapabilityAnalyzerFactoryRejectsMissingSafepointClassifier()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AllocationCapabilityAnalyzerFactory(null!));
    }

    [Fact]
    public void AnalysisRegistrationRejectsMissingServiceCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AnalysisServiceCollectionExtensions.AddCompilerAnalysis(null!));
    }

    [Fact]
    public void AnalysisRegistrationLeavesControlFlowRegistrationToItsFeature()
    {
        using var services = new ServiceCollection()
            .AddCompilerAnalysis()
            .BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            services.GetRequiredService<ITypedStackValidatorFactory>());
        Assert.Throws<InvalidOperationException>(() =>
            services.GetRequiredService<IControlFlowGraphBuilderFactory>());
        Assert.Throws<InvalidOperationException>(() =>
            services.GetRequiredService<IControlFlowGraphAnalyzerFactory>());
        Assert.Throws<InvalidOperationException>(() =>
            services.GetRequiredService<IValidatedStructuredMethodBuilderFactory>());
    }

    [Fact]
    public void AnalyzerFacadeDelegatesTheClosureBuildThroughItsCapability()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var entry = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            new EntityKey(assembly, 1),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        var closure = new RecordingClosureBuilder();
        var analyzer = new WholeProgramAnalyzer(closure);

        var result = analyzer.Analyze(entry, []);

        Assert.Null(result);
        Assert.Same(entry, closure.EntryPoint);
    }

    [Fact]
    public void AnalyzerFacadeRejectsMissingClosureCapability()
    {
        Assert.Throws<ArgumentNullException>(() => new WholeProgramAnalyzer(null!));
    }

    [Fact]
    public void AsyncBindingAdapterDelegatesItsSingleResolveCapability()
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            new EntityKey(assembly, 1),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        var resolver = new DelegateJavaScriptAsyncBindingResolver(_ => null);

        Assert.Null(resolver.Resolve(method));
    }

    [Fact]
    public void AsyncBindingAdapterRejectsMissingResolveCapability()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DelegateJavaScriptAsyncBindingResolver(null!));
    }

    [Fact]
    public void LedgerFactoryCreatesIndependentPerAnalysisLedgers()
    {
        var factory = new ReachabilityLedgerFactory();

        var first = factory.Create();
        var second = factory.Create();

        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void FactoryUsesEveryInjectedAlgorithmAndFreshLedgerPerAnalysis(bool constants, bool populations, bool calls)
    {
        using var assets = TestAssets.Create();
        using var compilation = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var snapshot = compilation.Snapshot;
        var types = MetadataCompilationTestActors.Types(snapshot);
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var methods = MetadataCompilationTestActors.Methods(snapshot);
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var typeFinder = MetadataCompilationTestActors.TypeFinder(snapshot);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = MetadataCompilationTestActors.TypeIdentities(snapshot);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(snapshot);
        var baseTypeIdentities = MetadataCompilationTestActors.BaseTypeIdentities(snapshot);
        var implementedInterfaces = MetadataCompilationTestActors.ImplementedInterfaces(snapshot);
        var methodImplementations = MetadataCompilationTestActors.MethodImplementations(snapshot);
        var methodBodies = MetadataCompilationTestActors.MethodBodies(snapshot);
        var methodFinder = MetadataCompilationTestActors.MethodFinder(snapshot);
        var calledMethods = new RecordingCalledMethodResolverFactory();
        var typeOperands = new RecordingTypeOperandResolverFactory();
        var specializers = new RecordingSpecializerFactory();
        var dispatchTargets = new RecordingDispatchTargetResolverFactory();
        var relationships = new RecordingTypeRelationshipClassifierFactory();
        var delegateTypes = new RecordingDelegateTypeRecognizerFactory();
        var typeTests = new RecordingTypeTestFactory();
        var intrinsics = new RecordingIntrinsicFactory();
        var exceptions = new RecordingExceptionFactory();
        var asyncBindings = new RecordingAsyncBindingFactory();
        var importClassifiers = new ReachabilityImportClassifierFactory(
            new EnumMetadataRequirementClassifier(),
            new NetWasm.Compiler.Core.NativeInterop.NativeDeclarationValidator());
        var builders = new RecordingProgramBuilderFactory();
        var delegateBindings = new RecordingDelegateBindingPlannerFactory();
        var typeClassifiers = new RecordingTypeClassifierFactory();
        var factory = new WholeProgramAnalyzerFactory(
            calledMethods,
            typeOperands,
            specializers,
            new ManagedMethodOptimizerFactory(
                new TypedStackValidatorFactory(
                    new StackTypeCompatibilityValidator()),
                new ControlFlowGraphBuilderFactory()),
            new AttributeQueryMethodSpecializerFactory(),
            dispatchTargets,
            relationships,
            new TypeRelationshipKeyResolverFactory(),
            delegateTypes,
            importClassifiers,
            new ReachabilityInstructionAnalyzerFactory(
                new NetWasm.Compiler.Core.Types.NullableTypeResolver(),
                new ManagedCallSiteFactory(new ManagedMethodIdentityFactory())),
            new ReachableMethodAnalyzerFactory(
                new TypedStackValidatorFactory(
                    new StackTypeCompatibilityValidator()),
                new ControlFlowGraphBuilderFactory()),
            typeTests,
            exceptions,
            builders,
            new ReachabilityLedgerFactory(),
            new AllocationCapabilityAnalyzerFactory(
                new RuntimeAllocationSafepointClassifier()),
            new ManagedByRefEffectAnalyzerFactory(new IndexedWorkExecutor(), new CompilerParallelism(1)),
            new BaseTypeResolverFactory(),
            new DelegateMethodClassifierFactory(),
            new DispatchSiteKeyBuilder(),
            delegateBindings,
            new ManagedCallSiteLedgerWriter(),
            new NullableTypeResolver(),
            new RecordingReachableMethodBatchObserver(),
            new RecordingReachabilityClosureObserver(),
            typeClassifiers,
            new ControlFlowGraphBuilderFactory());
        var settings = new WasmOptimizationSettingsResolver().Resolve(FinalWasmOptimization.Oz,
            new(ConstantArgumentPropagation: constants ? ManagedConstantArgumentPropagation.Enabled : ManagedConstantArgumentPropagation.Disabled,
                StaticArrayPopulationElimination: populations ? ManagedStaticArrayPopulationElimination.Enabled : ManagedStaticArrayPopulationElimination.Disabled,
                ConstantCallFolding: calls ? ManagedConstantCallFolding.Enabled : ManagedConstantCallFolding.Disabled));
        var entry = methodFinder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "NetWasm.Fixtures.Application.EntryPoint",
            "Run");

        var resolver = asyncBindings.Create(
            typeFinder,
            typeDefinitions,
            identities,
            fields,
            methods,
            methodInstances,
            symbols);
        var analyzer = factory.Create(
            snapshot,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypeIdentities,
            implementedInterfaces,
            methodBodies,
            methodInstances,
            methodImplementations,
            symbols,
            resolver.Resolve,
            intrinsics.Create(symbols, snapshot.Methods),
            settings,
            PlatformWorldCapabilities.None);
        var unaliasedSnapshot = snapshot with { ReferenceAssemblyAliases = null };
        _ = factory.Create(
            unaliasedSnapshot,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypeIdentities,
            implementedInterfaces,
            methodBodies,
            methodInstances,
            methodImplementations,
            symbols,
            resolver.Resolve,
            intrinsics.Create(symbols, snapshot.Methods),
            settings,
            PlatformWorldCapabilities.None);
        var referenceName = snapshot.Assemblies
            .SelectMany(assembly => assembly.References)
            .First();
        var aliasedSnapshot = snapshot with
        {
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty
                .Add(referenceName, referenceName),
        };
        _ = factory.Create(
            aliasedSnapshot,
            types,
            fields,
            methods,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypeIdentities,
            implementedInterfaces,
            methodBodies,
            methodInstances,
            methodImplementations,
            symbols,
            resolver.Resolve,
            intrinsics.Create(symbols, snapshot.Methods),
            settings,
            PlatformWorldCapabilities.None);
        foreach (var invalid in new[]
        {
            settings with { ConstantArgumentPropagation = (ConstantArgumentPropagationPolicy)255 },
            settings with { StaticArrayPopulationElimination = (StaticArrayPopulationEliminationPolicy)255 },
            settings with { ConstantCallFolding = (ConstantCallFoldingPolicy)255 },
            settings with { SwitchBitTests = (SwitchBitTestPolicy)255 },
            settings with { Devirtualization = (DevirtualizationPolicy)255 },
            settings with { AllocationElimination = (AllocationEliminationPolicy)255 },
        })
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => factory.Create(
                snapshot, types, fields, methods, typeFinder, typeDefinitions, identities,
                baseTypeIdentities, implementedInterfaces, methodBodies, methodInstances,
                methodImplementations, symbols, resolver.Resolve,
                intrinsics.Create(symbols, snapshot.Methods), invalid, PlatformWorldCapabilities.None));
            Assert.Equal("optimizationSettings", error.ParamName);
            Assert.Equal(3, typeClassifiers.Snapshots.Count);
        }
        var first = analyzer.Analyze(entry, []);
        var second = analyzer.Analyze(entry, []);

        Assert.True(calledMethods.Called);
        Assert.True(typeOperands.Called);
        Assert.True(specializers.Called);
        Assert.True(dispatchTargets.Called);
        Assert.True(relationships.Called);
        Assert.True(delegateTypes.Called);
        Assert.True(typeTests.Called);
        Assert.True(intrinsics.Called);
        Assert.True(exceptions.Called);
        Assert.True(asyncBindings.Called);
        Assert.True(builders.Called);
        Assert.True(delegateBindings.Called);
        Assert.Equal(3, typeClassifiers.Snapshots.Count);
        Assert.Same(snapshot, typeClassifiers.Snapshots[0]);
        Assert.Same(unaliasedSnapshot, typeClassifiers.Snapshots[1]);
        Assert.Same(aliasedSnapshot, typeClassifiers.Snapshots[2]);
        Assert.Equal(first.Methods.Keys, second.Methods.Keys);
        Assert.NotSame(first, second);

    }

    private sealed class RecordingTypeClassifierFactory : ITypeClassifierFactory
    {
        public List<MetadataCompilationSnapshot> Snapshots { get; } = [];

        public ITypeClassifier Create(MetadataCompilationSnapshot snapshot)
        {
            Snapshots.Add(snapshot);
            return MetadataCompilationTestActors.TypeClassifier(snapshot);
        }
    }

    private sealed class RecordingCalledMethodResolverFactory :
        ICalledMethodResolverFactory
    {
        public bool Called { get; private set; }

        public ICalledMethodResolver Create(
            IMethodRepository methods,
            IMethodInstanceResolver methodInstances,
            ISymbolFormatter symbols)
        {
            Called = true;
            return new CalledMethodResolver(methods, methodInstances, symbols);
        }
    }

    private sealed class RecordingClosureBuilder : IReachabilityClosureBuilder
    {
        public MethodDefinitionModel? EntryPoint { get; private set; }

        public ReachableProgram Build(
            MethodDefinitionModel? entryPoint,
            IEnumerable<ProgramExport> requestedExports,
            ReachabilityRoots? roots = null,
            ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
        {
            EntryPoint = entryPoint;
            return null!;
        }
    }

    private sealed class RecordingTypeOperandResolverFactory :
        ITypeOperandResolverFactory
    {
        public bool Called { get; private set; }

        public ITypeOperandResolver Create(ITypeIdentityResolver types)
        {
            Called = true;
            return new TypeOperandResolver(types);
        }
    }

    private sealed class RecordingSpecializerFactory : IMethodSpecializerFactory
    {
        public bool Called { get; private set; }

        public IMethodSpecializer Create(
            ITypeDefinitionResolver typeDefinitions,
            ITypeRepository typeRepository,
            IMethodRepository methods,
            IMethodInstanceResolver methodInstances,
            ISymbolFormatter symbols,
            IFieldRepository fields,
            ITypeFinder types,
            ICalledMethodResolver calledMethods,
            IMethodImplementationResolver methodImplementations,
            ITypeRelationshipClassifier relationships,
            IMethodBodyReader methodBodies,
            WasmOptimizationSettings optimizationSettings,
            IRuntimeIntrinsicRegistry intrinsics,
            PlatformWorldCapabilities world,
            ITypeIdentityResolver typeIdentities)
        {
            Called = true;
            return new MethodSpecializerFactory(
                new ControlFlowGraphBuilderFactory(),
                new ControlFlowGraphAnalyzerFactory(),
                new TypedStackValidatorFactory(new StackTypeCompatibilityValidator())).Create(
                typeDefinitions,
                typeRepository,
                methods,
                methodInstances,
                symbols,
                fields,
                types,
                calledMethods,
                methodImplementations,
                relationships,
                methodBodies,
                optimizationSettings,
                intrinsics,
                world,
                typeIdentities);
        }
    }

    private sealed class RecordingDispatchTargetResolverFactory :
        IDispatchTargetResolverFactory
    {
        public bool Called { get; private set; }

        public IDispatchTargetResolver Create(
            ITypeFinder types,
            ITypeDefinitionResolver typeDefinitions,
            IMethodRepository methods,
            IMethodImplementationResolver methodImplementations,
            IImplementedInterfaceResolver implementedInterfaces,
            IMethodInstanceResolver methodInstances,
            ISymbolFormatter symbols,
            ITypeRelationshipClassifier relationships,
            IBaseTypeResolver baseTypes)
        {
            Called = true;
            return new DispatchTargetResolverFactory().Create(
                types,
                typeDefinitions,
                methods,
                methodImplementations,
                implementedInterfaces,
                methodInstances,
                symbols,
                relationships,
                baseTypes);
        }
    }

    private sealed class RecordingTypeRelationshipClassifierFactory :
        ITypeRelationshipClassifierFactory
    {
        public bool Called { get; private set; }

        public ITypeRelationshipClassifier Create(
            ITypeFinder types,
            ITypeDefinitionResolver typeDefinitions,
            ITypeIdentityResolver identities,
            IImplementedInterfaceResolver interfaces,
            IBaseTypeResolver baseTypes)
        {
            Called = true;
            return new TestTypeRelationshipClassifier();
        }
    }

    private sealed class RecordingDelegateTypeRecognizerFactory :
        IDelegateTypeRecognizerFactory
    {
        public bool Called { get; private set; }

        public IDelegateTypeRecognizer Create(
            ITypeDefinitionResolver typeDefinitions,
            IBaseTypeResolver baseTypes)
        {
            Called = true;
            return new DelegateTypeRecognizerFactory().Create(
                typeDefinitions,
                baseTypes);
        }
    }

    private sealed class RecordingTypeTestFactory : ITypeTestPlannerFactory
    {
        public bool Called { get; private set; }

        public ITypeTestPlanner Create(
            ITypeOperandResolver typeOperands,
            ITypeRelationshipClassifier relationships,
            ITypeRelationshipKeyResolver relationshipKeys)
        {
            Called = true;
            return new TypeTestPlanner(
                typeOperands,
                relationships,
                relationshipKeys,
                new NullableTypeResolver());
        }
    }

    private sealed class RecordingIntrinsicFactory : IRuntimeIntrinsicRegistryFactory
    {
        public bool Called { get; private set; }

        public IRuntimeIntrinsicRegistry Create(
            ISymbolFormatter symbols,
            IEnumerable<MethodDefinitionModel> methods)
        {
            Called = true;
            return new RuntimeIntrinsicRegistry(symbols, methods);
        }
    }

    private sealed class RecordingExceptionFactory : IImplicitExceptionDiscoveryFactory
    {
        public bool Called { get; private set; }

        public IImplicitExceptionDiscovery Create(
            IMethodRepository methods,
            ISymbolFormatter symbols,
            IRuntimeIntrinsicRegistry intrinsics)
        {
            Called = true;
            return new ImplicitExceptionDiscovery(
                methods,
                symbols,
                new RuntimeIntrinsicExceptionRequirementProvider(intrinsics));
        }
    }

    private sealed class RecordingProgramBuilderFactory : IReachableProgramBuilderFactory
    {
        public bool Called { get; private set; }

        public IReachableProgramBuilder Create(
            IAllocationCapabilityAnalyzer allocationCapabilities,
            IManagedByRefEffectAnalyzer byRefEffects,
            IDelegateBindingPlanner delegateBindings)
        {
            Called = true;
            return new ReachableProgramBuilder(
                allocationCapabilities,
                byRefEffects,
                delegateBindings);
        }
    }

    private sealed class RecordingDelegateBindingPlannerFactory :
        IDelegateBindingPlannerFactory
    {
        public bool Called { get; private set; }

        public IDelegateBindingPlanner Create(
            ITypeRelationshipClassifier relationships)
        {
            Called = true;
            return new DelegateBindingPlanner(
                relationships,
                new ManagedMethodIdentityFactory());
        }
    }

    private sealed class RecordingAsyncBindingFactory :
        IJavaScriptAsyncBindingResolverFactory
    {
        public bool Called { get; private set; }

        public IJavaScriptAsyncBindingResolver Create(
            ITypeFinder types,
            ITypeDefinitionResolver typeDefinitions,
            ITypeIdentityResolver identities,
            IFieldRepository fields,
            IMethodRepository methods,
            IMethodInstanceResolver methodInstances,
            ISymbolFormatter symbols)
        {
            Called = true;
            return new JavaScriptAsyncBindingResolver(
                types,
                typeDefinitions,
                identities,
                fields,
                methods,
                methodInstances,
                symbols);
        }
    }

}
