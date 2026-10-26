using System.Collections.Immutable;
using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationPipelineCompositionTests
{
    [Fact]
    public void EveryStageCanBeDecoratedDuringSuccessfulCompilation()
    {
        using var assets = TestAssets.Create();
        using var defaults = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var calls = new List<string>();
        var metadata = new RecordingMetadataLoader(
            defaults.GetRequiredService<IMetadataCompilationLoader>(),
            calls);
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IMetadataCompilationLoader>(metadata);
        services.AddSingleton<IInteropDeclarationValidator>(new RecordingInteropValidator(
            defaults.GetRequiredService<IInteropDeclarationValidator>(), calls));
        services.AddSingleton<ICompilationEntryResolver>(new RecordingEntryResolver(
            defaults.GetRequiredService<ICompilationEntryResolver>(), calls));
        services.AddSingleton<IWholeProgramAnalyzerFactory>(new RecordingAnalyzerFactory(
            defaults.GetRequiredService<IWholeProgramAnalyzerFactory>(), calls));
        services.AddSingleton<IManagedLayoutCompiler>(new RecordingLayoutCompiler(
            defaults.GetRequiredService<IManagedLayoutCompiler>(), calls));
        var rootMaps = new RecordingRootMapFactory(
            defaults.GetRequiredService<IRootMapAnalyzerFactory>(), calls);
        services.AddSingleton<IRootMapAnalyzerFactory>(rootMaps);
        services.AddSingleton<IWasmMethodLowerer>(new RecordingWasmMethodLowerer(
            defaults.GetRequiredService<IWasmMethodLowerer>(), calls));
        services.AddSingleton<IManagedWasmEmitterFactory>(new RecordingEmitterFactory(
            defaults.GetRequiredService<IManagedWasmEmitterFactory>(), calls));
        services.AddSingleton<IMetadataInvariantValidator>(new RecordingMetadataInvariant(
            defaults.GetRequiredService<IMetadataInvariantValidator>(), calls));
        services.AddSingleton<IStructuredProgramInvariantValidator>(new RecordingStructuredInvariant(
            defaults.GetRequiredService<IStructuredProgramInvariantValidator>(), calls));
        services.AddSingleton<IReachabilityInvariantValidator>(new RecordingReachabilityInvariant(
            defaults.GetRequiredService<IReachabilityInvariantValidator>()));
        services.AddSingleton<ILayoutInvariantValidator>(new RecordingLayoutInvariant(
            defaults.GetRequiredService<ILayoutInvariantValidator>(), calls));
        var rootInvariants = new RecordingRootMapInvariant(
            defaults.GetRequiredService<IRootMapInvariantValidator>(), calls);
        services.AddSingleton<IRootMapInvariantValidator>(rootInvariants);
        services.AddSingleton<IWasmEmissionInvariantValidator>(new RecordingEmissionInvariant(
            defaults.GetRequiredService<IWasmEmissionInvariantValidator>(), calls));
        services.AddSingleton<ICompilerDiagnosticTraceWriter>(new RecordingTraceWriter(
            defaults.GetRequiredService<ICompilerDiagnosticTraceWriter>(), calls));
        services.AddSingleton<IHostInteropManifestBuilder>(new RecordingManifestBuilder(
            defaults.GetRequiredService<IHostInteropManifestBuilder>(), calls));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var tracePath = Path.Combine(assets.Directory, "decorated.trace.txt");
        var options = CompilerTestSupport.Options(assets, []) with
        {
            DiagnosticTracePath = tracePath,
        };

        var result = provider.GetRequiredService<INetWasmCompiler>().Compile(options);

        Assert.NotEmpty(result.ApplicationModule);
        Assert.NotEmpty(result.FunctionImports);
        Assert.True(File.Exists(tracePath));
        Assert.True(metadata.MetadataDisposed);
        Assert.NotNull(rootMaps.TypeClassifier);
        Assert.Same(rootMaps.TypeClassifier, rootInvariants.TypeClassifier);
        AssertOrdered(calls,
            "metadata-load",
            "invariant-metadata",
            "interop",
            "entry",
            "analysis-create",
            "analysis",
            "layout",
            "invariant-layouts",
            "roots-create",
            "roots",
            "invariant-roots",
            "wasm-lower",
            "invariant-program",
            "emitter-create",
            "emit",
            "invariant-emission",
            "trace",
            "manifest",
            "metadata-dispose");
    }

    [Fact]
    public void RegistrationBuildsWithScopeValidationEnabled()
    {
        using var provider = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        Assert.NotNull(provider.GetRequiredService<INetWasmCompiler>());
    }

    [Fact]
    public async Task StaticAndInjectedCompilationRemainEquivalentAndConcurrentSafe()
    {
        using var assets = TestAssets.Create();
        var options = CompilerTestSupport.Options(assets, []);
        var expected = NetWasmCompiler.Compile(options);
        using var provider = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        var compiler = provider.GetRequiredService<INetWasmCompiler>();

        var compilations = await Task.WhenAll(
            Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() => compiler.Compile(options))));

        Assert.All(compilations, result =>
            Assert.Equal(expected.ApplicationModule, result.ApplicationModule));
        Assert.Equal(
            compilations.Length,
            compilations.Select(result => result.Program).Distinct().Count());
        Assert.Equal(
            compilations.Length,
            compilations.Select(result => result.Layouts).Distinct().Count());
    }

    [Fact]
    public void CompilationIncludesARequestedExportInTheEmissionRequest()
    {
        using var assets = TestAssets.Create();
        using var provider = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        var result = provider.GetRequiredService<INetWasmCompiler>().Compile(
            CompilerTestSupport.Options(
                assets,
                [new RequestedExport(
                    "extra",
                    "NetWasm.Fixtures.Application.EntryPoint",
                    "Run")]));

        Assert.NotEmpty(result.ApplicationModule);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RequestedEnumExportsUseSelectedNormalizedSignatures(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var contracts = assets.CompileSource(
            "RequestedEnumExportContracts",
            """
            namespace RequestedEnumExportContracts;

            public enum Narrow : int
            {
                None,
            }

            public enum Wide : long
            {
                None,
            }
            """);
        var assembly = assets.CompileSource(
            "RequestedEnumExportFixture",
            """
            using RequestedEnumExportContracts;

            namespace RequestedEnumExportFixture;

            public static class EntryPoint
            {
                public static int Run(int value) => value;

                public static Narrow EchoNarrow(Narrow value) => value;

                public static Wide EchoWide(Wide value) => value;
            }
            """,
            contracts);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [contracts, assets.CoreLib],
            "RequestedEnumExportFixture.EntryPoint",
            "Run",
            [
                new("narrow", "RequestedEnumExportFixture.EntryPoint", "EchoNarrow"),
                new("wide", "RequestedEnumExportFixture.EntryPoint", "EchoWide"),
            ],
            target));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            42,
            target,
            result.Layouts.StaticDataEnd,
            entryExportName: "narrow"));
        foreach (var input in new[] { 42L, long.MinValue, long.MaxValue, 9007199254740993L })
        {
            Assert.Equal(input, CompilerTestSupport.ExecuteWithStandardWasiNode(
                result.ApplicationModule,
                assets.Directory,
                input,
                target,
                result.Layouts.StaticDataEnd,
                entryExportName: "wide"));
        }
    }

    [Fact]
    public void CompilationAcceptsReferenceAssemblyAliases()
    {
        using var assets = TestAssets.Create();
        using var provider = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        var options = CompilerTestSupport.Options(assets, []) with
        {
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add(
                "System.Runtime",
                "NetWasm.CoreLib"),
        };

        Assert.NotEmpty(provider.GetRequiredService<INetWasmCompiler>().Compile(options)
            .ApplicationModule);
    }

    private static void AssertOrdered(IReadOnlyList<string> actual, params string[] expected)
    {
        var position = -1;
        foreach (var value in expected)
        {
            position = actual.IndexOf(value, position + 1);
            Assert.True(position >= 0, $"Missing ordered stage '{value}'. Calls: {string.Join(", ", actual)}");
        }
    }

    private sealed class RecordingMetadataLoader(
        IMetadataCompilationLoader inner,
        List<string> calls) : IMetadataCompilationLoader
    {
        private bool _metadataDisposed;
        public bool MetadataDisposed => _metadataDisposed;

        public RecordingMetadataLease Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths)
        {
            calls.Add("metadata-load");
            return new RecordingMetadataLease(
                inner.Load(entryAssemblyPath, referencePaths),
                calls,
                () => _metadataDisposed = true);
        }

        public RecordingMetadataLease Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths,
            ImmutableDictionary<string, string>? aliases)
        {
            calls.Add("metadata-load");
            return new RecordingMetadataLease(
                inner.Load(entryAssemblyPath, referencePaths, aliases),
                calls,
                () => _metadataDisposed = true);
        }

        IMetadataCompilationLease IMetadataCompilationLoader.Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths,
            ImmutableDictionary<string, string>? aliases) =>
            Load(entryAssemblyPath, referencePaths, aliases);
    }

    private sealed class RecordingMetadataLease(
        IMetadataCompilationLease inner,
        List<string> calls,
        Action onDispose) : IMetadataCompilationLease
    {
        public bool Disposed { get; private set; }
        public MetadataCompilationSnapshot Snapshot => inner.Snapshot;

        public void Dispose()
        {
            Disposed = true;
            onDispose();
            calls.Add("metadata-dispose");
            inner.Dispose();
        }
    }

    private sealed class RecordingInteropValidator(
        IInteropDeclarationValidator inner,
        List<string> calls) : IInteropDeclarationValidator
    {
        public void Validate(
            MetadataCompilationSnapshot metadata,
            ITypeDefinitionResolver typeDefinitions,
            IBaseTypeResolver baseTypes,
            IMethodRepository methods,
            ISymbolFormatter symbols)
        {
            calls.Add("interop");
            inner.Validate(
                metadata,
                typeDefinitions,
                baseTypes,
                methods,
                symbols);
        }
    }

    private sealed class RecordingEntryResolver(
        ICompilationEntryResolver inner,
        List<string> calls) : ICompilationEntryResolver
    {
        public CompilationEntry Resolve(
            MetadataCompilationSnapshot metadata,
            IMethodFinder methods,
            ISymbolFormatter symbols,
            CompilerOptions options)
        {
            calls.Add("entry");
            return inner.Resolve(metadata, methods, symbols, options);
        }
    }

    private sealed class RecordingAnalyzerFactory(
        IWholeProgramAnalyzerFactory inner,
        List<string> calls) : IWholeProgramAnalyzerFactory
    {
        public IWholeProgramAnalyzer Create(
            MetadataCompilationSnapshot metadata,
            ITypeRepository typeRepository,
            IFieldRepository fieldRepository,
            IMethodRepository methodRepository,
            ITypeFinder typeFinder,
            ITypeDefinitionResolver typeDefinitions,
            ITypeIdentityResolver typeIdentities,
            IBaseTypeIdentityResolver baseTypeIdentities,
            IImplementedInterfaceResolver implementedInterfaces,
            IMethodBodyReader methodBodies,
            IMethodInstanceResolver methodInstances,
            IMethodImplementationResolver methodImplementations,
            ISymbolFormatter symbols,
            Func<MethodDefinitionModel, JavaScriptAsyncMethodBinding?> javaScriptAsyncBindings,
            IRuntimeIntrinsicRegistry intrinsics,
            WasmOptimizationSettings optimizationSettings,
            PlatformWorldCapabilities world)
        {
            calls.Add("analysis-create");
            return new RecordingAnalyzer(inner.Create(
                metadata,
                typeRepository,
                fieldRepository,
                methodRepository,
                typeFinder,
                typeDefinitions,
                typeIdentities,
                baseTypeIdentities,
                implementedInterfaces,
                methodBodies,
                methodInstances,
                methodImplementations,
                symbols,
                javaScriptAsyncBindings,
                intrinsics,
                optimizationSettings,
                world), calls);
        }
    }

    private sealed class RecordingAnalyzer(
        IWholeProgramAnalyzer inner,
        List<string> calls) : IWholeProgramAnalyzer
    {
        public ReachableProgram Analyze(
            MethodDefinitionModel? entryPoint,
            IEnumerable<ProgramExport> requestedExports,
            ReachabilityRoots? roots = null)
        {
            calls.Add("analysis");
            return inner.Analyze(entryPoint, requestedExports, roots);
        }
    }

    private sealed class RecordingLayoutCompiler(
        IManagedLayoutCompiler inner,
        List<string> calls) : IManagedLayoutCompiler
    {
        public ManagedLayoutSnapshot Compile(
            MetadataCompilationSnapshot metadata,
            ITypeRepository typeRepository,
            IFieldRepository fields,
            ITypeFinder types,
            ITypeDefinitionResolver typeDefinitions,
            ITypeIdentityResolver identities,
            IMetadataEntityBaseTypeResolver entityBaseTypes,
            IMetadataIdentityBaseTypeResolver identityBaseTypes,
            ReachableProgram program,
            WasmTarget target)
        {
            calls.Add("layout");
            return inner.Compile(
                metadata,
                typeRepository,
                fields,
                types,
                typeDefinitions,
                identities,
                entityBaseTypes,
                identityBaseTypes,
                program,
                target);
        }
    }

    private sealed class RecordingRootMapFactory(
        IRootMapAnalyzerFactory inner,
        List<string> calls) : IRootMapAnalyzerFactory
    {
        public ITypeClassifier? TypeClassifier { get; private set; }

        public IRootMapAnalyzer Create(
            ITypeRepository types,
            IFieldRepository fields,
            IMethodRepository methods,
            IValueLayoutProvider layouts,
            IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites,
            ITypeClassifier typeClassifier)
        {
            calls.Add("roots-create");
            TypeClassifier = typeClassifier;
            return new RecordingRootMapAnalyzer(
                inner.Create(types, fields, methods, layouts, dispatchCallSites, typeClassifier),
                calls);
        }
    }

    private sealed class RecordingRootMapAnalyzer(
        IRootMapAnalyzer inner,
        List<string> calls) : IRootMapAnalyzer
    {
        public MethodRootMap Analyze(RootMapAnalysisRequest request)
        {
            Record();
            return inner.Analyze(request);
        }

        private void Record()
        {
            if (!calls.Contains("roots"))
            {
                calls.Add("roots");
            }
        }
    }

    private sealed class RecordingEmitterFactory(
        IManagedWasmEmitterFactory inner,
        List<string> calls) : IManagedWasmEmitterFactory
    {
        public IManagedModuleEmitter Create(
            ITypeRepository types,
            ITypeDefinitionResolver typeDefinitions,
            IFieldRepository fields,
            IMethodRepository methods,
            ISymbolFormatter symbols,
            ITypeClassifier typeClassifier,
            IRuntimeIntrinsicRegistry intrinsics,
            ITargetLayout targetLayout,
            IValueLayoutProvider valueLayouts,
            ITypeLayoutProvider typeLayouts,
            IInstanceFieldLayoutProvider instanceFields,
            IStaticFieldLayoutProvider staticFields,
            IStaticDataLayout staticData,
            IMemberDescriptorLayout memberDescriptors,
            IRuntimeObjectLayout runtimeObjects,
            IManagedExceptionObjectProvider exceptionObjects,
            ITypeDescriptorSource typeDescriptors)
        {
            calls.Add("emitter-create");
            return new RecordingEmitter(inner.Create(
                types,
                typeDefinitions,
                fields,
                methods,
                symbols,
                typeClassifier,
                intrinsics,
                targetLayout,
                valueLayouts,
                typeLayouts,
                instanceFields,
                staticFields,
                staticData,
                memberDescriptors,
                runtimeObjects,
                exceptionObjects,
                typeDescriptors), calls);
        }
    }

    private sealed class RecordingWasmMethodLowerer(
        IWasmMethodLowerer inner,
        List<string> calls) : IWasmMethodLowerer
    {
        public StructuredMethod Lower(
            ManagedMethodBody method,
            BranchPrefixDuplicationPolicy branchPrefixDuplication =
                BranchPrefixDuplicationPolicy.SizeAware)
        {
            if (!calls.Contains("wasm-lower"))
            {
                calls.Add("wasm-lower");
            }
            return inner.Lower(method, branchPrefixDuplication);
        }
    }

    private sealed class RecordingEmitter(
        IManagedModuleEmitter inner,
        List<string> calls) : IManagedModuleEmitter
    {
        public WasmModuleEmissionResult Emit(WasmEmissionRequest request)
        {
            calls.Add("emit");
            return inner.Emit(request);
        }
    }

    private sealed class RecordingMetadataInvariant(
        IMetadataInvariantValidator inner,
        List<string> calls) : IMetadataInvariantValidator
    {
        public void Validate(
            MetadataCompilationSnapshot metadata,
            ITypeRepository types,
            ISymbolFormatter symbols)
        {
            calls.Add("invariant-metadata");
            inner.Validate(metadata, types, symbols);
        }
    }

    private sealed class RecordingStructuredInvariant(
        IStructuredProgramInvariantValidator inner,
        List<string> calls) : IStructuredProgramInvariantValidator
    {
        public void Validate(
            ISymbolFormatter symbols,
            WasmMethodLoweringResult program)
        {
            calls.Add("invariant-program");
            inner.Validate(symbols, program);
        }
    }

    private sealed class RecordingReachabilityInvariant(
        IReachabilityInvariantValidator inner) : IReachabilityInvariantValidator
    {
        public void Validate(
            IMethodInstanceResolver methodInstances,
            ITypeClassifier types,
            ReachableProgram program,
            IRuntimeIntrinsicRegistry intrinsics) =>
            inner.Validate(methodInstances, types, program, intrinsics);
    }

    private sealed class RecordingLayoutInvariant(
        ILayoutInvariantValidator inner,
        List<string> calls) : ILayoutInvariantValidator
    {
        public void Validate(ITargetLayout layouts, WasmTarget target)
        {
            calls.Add("invariant-layouts");
            inner.Validate(layouts, target);
        }
    }

    private sealed class RecordingRootMapInvariant(
        IRootMapInvariantValidator inner,
        List<string> calls) : IRootMapInvariantValidator
    {
        public ITypeClassifier? TypeClassifier { get; private set; }

        public void Validate(
            ITypeRepository types,
            IFieldRepository fields,
            IMethodRepository methods,
            ReachableProgram program,
            ITypeClassifier typeClassifier)
        {
            calls.Add("invariant-roots");
            TypeClassifier = typeClassifier;
            inner.Validate(types, fields, methods, program, typeClassifier);
        }
    }

    private sealed class RecordingEmissionInvariant(
        IWasmEmissionInvariantValidator inner,
        List<string> calls) : IWasmEmissionInvariantValidator
    {
        public void Validate(ReadOnlySpan<byte> bytes, WasmTarget target)
        {
            calls.Add("invariant-emission");
            inner.Validate(bytes, target);
        }
    }

    private sealed class RecordingTraceWriter(
        ICompilerDiagnosticTraceWriter inner,
        List<string> calls) : ICompilerDiagnosticTraceWriter
    {
        public void WriteTrace(
            string path,
            ISymbolFormatter symbols,
            ITypeRepository types,
            IFieldRepository fields,
            ReachableProgram program,
            WasmMethodLoweringResult lowering,
            ManagedLayoutSnapshot layouts,
            ITypeLayoutProvider typeLayouts,
            IInstanceFieldLayoutProvider instanceFields,
            IStaticFieldLayoutProvider staticFields,
            MethodDefinitionModel? entryPoint,
            WasmTarget target,
            int staticDataEnd)
        {
            calls.Add("trace");
            inner.WriteTrace(
                path,
                symbols,
                types,
                fields,
                program,
                lowering,
                layouts,
                typeLayouts,
                instanceFields,
                staticFields,
                entryPoint,
                target,
                staticDataEnd);
        }
    }

    private sealed class RecordingManifestBuilder(
        IHostInteropManifestBuilder inner,
        List<string> calls) : IHostInteropManifestBuilder
    {
        public HostInteropManifest Build(
            ReachableProgram program,
            ImmutableArray<ProgramExport> exports,
            ManagedLayoutSnapshot layouts,
            ImmutableArray<string> importModules)
        {
            calls.Add("manifest");
            return inner.Build(program, exports, layouts, importModules);
        }
    }
}

internal static class StageCallListExtensions
{
    public static int IndexOf(
        this IReadOnlyList<string> values,
        string value,
        int startIndex)
    {
        for (var index = startIndex; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }
        return -1;
    }
}
