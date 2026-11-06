using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission;

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Loading;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDependencyInjectionTests
{
    [Fact]
    public void RegistrationProvidesOneInjectableCompilationPipeline()
    {
        var collection = new ServiceCollection();

        var returned = collection.AddNetWasmCompiler();
        using var services = collection.BuildServiceProvider();

        Assert.Same(collection, returned);
        Assert.IsType<CompilerDiagnosticCompilationDecorator>(
            services.GetRequiredService<INetWasmCompiler>());
        Assert.IsType<NetWasmCompilationPipeline>(
            services.GetRequiredService<NetWasmCompilationPipeline>());
        Assert.Same(
            services.GetRequiredService<INetWasmCompiler>(),
            services.GetRequiredService<INetWasmCompiler>());
        Assert.IsType<MetadataCompilationLoader>(
            services.GetRequiredService<IMetadataCompilationLoader>());
        Assert.IsType<MetadataMethodBodyReaderFactory>(
            services.GetRequiredService<IMethodBodyReaderFactory>());
        Assert.NotNull(services.GetRequiredService<IWholeProgramAnalyzerFactory>());
        Assert.NotNull(services.GetRequiredService<IManagedLayoutCompiler>());
        Assert.IsType<RootMapAnalyzerFactory>(
            services.GetRequiredService<IRootMapAnalyzerFactory>());
        Assert.IsType<ManagedWasmEmitterFactory>(
            services.GetRequiredService<IManagedWasmEmitterFactory>());
        Assert.IsType<WasmModuleEmitterFactory>(
            services.GetRequiredService<IWasmModuleEmitterFactory>());
        Assert.IsType<CompilationPipelineExecutor>(
            services.GetRequiredService<ICompilationPipelineExecutor>());
        Assert.IsType<CompilationMetadataStage>(
            services.GetRequiredService<ICompilationMetadataStage>());
        Assert.IsType<CompilationDiagnosticTraceStage>(
            services.GetRequiredService<ICompilationDiagnosticTraceStage>());
        Assert.IsType<CompilationComplexityStage>(
            services.GetRequiredService<ICompilationComplexityStage>());
        Assert.IsType<CompilationDiagnosticBindingStage>(
            services.GetRequiredService<ICompilationDiagnosticBindingStage>());
        Assert.IsType<ComponentExportMerger>(
            services.GetRequiredService<IComponentExportMerger>());
        Assert.IsType<ComponentReachabilityValidator>(
            services.GetRequiredService<IComponentReachabilityValidator>());
    }

    [Fact]
    public void RegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddNetWasmCompiler());
    }

    [Fact]
    public void PipelineRegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddCompilerPipeline());
    }

    [Fact]
    public void RegistrationAllowsACompilerStageToBeReplacedForFocusedTests()
    {
        var collection = new ServiceCollection();
        collection.AddNetWasmCompiler();
        collection.AddSingleton<IMetadataCompilationLoader, RecordingMetadataLoader>();
        using var services = collection.BuildServiceProvider();
        var compiler = services.GetRequiredService<INetWasmCompiler>();
        var options = new CompilerOptions(
            "entry.dll",
            [],
            "Application",
            "Run",
            []);

        var exception = Assert.Throws<StageReplacementObservedException>(() =>
            compiler.Compile(options));

        Assert.Equal("entry.dll", exception.EntryAssemblyPath);
    }

    [Fact]
    public void CompilationFacadeDelegatesToThePipelineExecutor()
    {
        var options = new CompilerOptions("entry.dll", [], "Application", "Run", []);
        var executor = new RecordingPipelineExecutor();
        var compiler = new NetWasmCompilationPipeline(executor);

        Assert.Throws<PipelineExecutionObservedException>(() => compiler.Compile(options));
        Assert.Same(options, executor.Options);
    }

    // The facade asks for a full compile: an executor that served it from the last compile
    // breaks its contract.
    [Fact]
    public void CompilationFacadeRefusesACompileServedFromTheLastOne()
    {
        var compiler = new NetWasmCompilationPipeline(new ServingPipelineExecutor());

        Assert.Throws<InvalidOperationException>(() =>
            compiler.Compile(new CompilerOptions("entry.dll", [], "Application", "Run", [])));
    }

    [Fact]
    public void CompilationFacadeRejectsAMissingPipelineExecutor()
    {
        Assert.Throws<ArgumentNullException>(() => new NetWasmCompilationPipeline(null!));
    }

    [Fact]
    public void CompilationFacadeRejectsMissingOptions()
    {
        var compiler = new NetWasmCompilationPipeline(new RecordingPipelineExecutor());

        Assert.Throws<ArgumentNullException>(() => compiler.Compile(null!));
        Assert.Throws<ArgumentNullException>(() => NetWasmCompiler.Compile(null!));
    }

    private sealed class RecordingMetadataLoader : IMetadataCompilationLoader
    {
        IMetadataCompilationLease IMetadataCompilationLoader.Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths,
            System.Collections.Immutable.ImmutableDictionary<string, string>? aliases) =>
            throw new StageReplacementObservedException(entryAssemblyPath);
    }

    private sealed class StageReplacementObservedException(string entryAssemblyPath) : Exception
    {
        public string EntryAssemblyPath { get; } = entryAssemblyPath;
    }

    private sealed class ServingPipelineExecutor : ICompilationPipelineExecutor
    {
        public CompilationOutcome Execute(CompilerOptions options, bool serveFromLastCompile = false) =>
            new(null, new ModuleCompilationResult([0x00, 0x61, 0x73, 0x6D], null!, 0));
    }

    private sealed class RecordingPipelineExecutor : ICompilationPipelineExecutor
    {
        public CompilerOptions? Options { get; private set; }

        public CompilationOutcome Execute(CompilerOptions options, bool serveFromLastCompile = false)
        {
            Options = options;
            throw new PipelineExecutionObservedException();
        }
    }

    private sealed class PipelineExecutionObservedException : Exception;
}
