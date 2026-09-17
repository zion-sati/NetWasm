using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Composition;

internal static class CompilerTaskComposition
{
    private const string DisableExperimentalWarningOption =
        "--disable-warning=ExperimentalWarning";

    public static IManagedModuleCompilationSessionFactory
        CreateManagedModuleCompilationSessionFactory() =>
        new ManagedModuleCompilationSessionFactory(CreateManagedModuleCompilationSession);

    public static ICompilerArtifactWriter CreateArtifactWriter() =>
        new CompilerArtifactWriter();

    public static ICompilerArtifactManifestTaskRequestBuilder CreateArtifactManifestTaskRequestBuilder() =>
        new CompilerArtifactManifestTaskRequestBuilder();

    public static ICompilerArtifactManifestBuilder CreateArtifactManifestBuilder() =>
        new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator());

    public static ICompilerArtifactManifestWriter CreateArtifactManifestWriter() =>
        new CompilerArtifactManifestWriter();

    public static ICompilerArtifactManifestValidator CreateArtifactManifestValidator() =>
        new CompilerArtifactManifestValidator(
            CreateArtifactManifestBuilder(),
            new CompilerArtifactManifestReader());

    public static IComponentBuildSessionFactory CreateComponentBuildSessionFactory() =>
        new ComponentBuildSessionFactory(CreateComponentBuildSession);

    public static IRawModuleLinkSessionFactory CreateRawModuleLinkSessionFactory() =>
        new RawModuleLinkSessionFactory(CreateRawModuleLinkSession);

    public static IRawBindingSessionFactory CreateRawBindingSessionFactory() =>
        new RawBindingSessionFactory(CreateRawBindingSession);

    public static IWitFunctionProjectionSessionFactory
        CreateWitFunctionProjectionSessionFactory() =>
        new WitFunctionProjectionSessionFactory(CreateWitFunctionProjectionSession);

    public static IByteArtifactWriter CreateByteArtifactWriter() =>
        new ByteArtifactWriter();

    public static IComponentManifestInputsReader CreateComponentManifestInputsReader() =>
        new ComponentManifestInputsReader(CreateHostInteropManifestReader());

    public static IComponentWitWorldSelector CreateComponentWitWorldSelector() =>
        new ComponentWitWorldSelector();

    public static ICompilerBuildMetadataReader CreateCompilerBuildMetadataReader() =>
        new CompilerBuildMetadataReader();

    public static ExternalToolCommand CreateWasmToolsCommand(
        string nodePath,
        string commandPath,
        string modulePath) => new(
            nodePath,
            [DisableExperimentalWarningOption, commandPath, modulePath]);

    public static BinaryenToolRunnerConfiguration CreateBinaryenConfiguration(
        string nodePath,
        string wasmOptPath,
        string wasmMergePath,
        string? nativeWasmOptPath = null,
        string? nativeWasmMergePath = null) => new(
            nodePath,
            [
                new(BinaryenToolIds.WasmOpt, wasmOptPath),
                new(BinaryenToolIds.WasmMerge, wasmMergePath),
            ],
            CreateNativeBinaryenTools(nativeWasmOptPath, nativeWasmMergePath));

    private static System.Collections.Immutable.ImmutableArray<BinaryenNativeTool>
        CreateNativeBinaryenTools(string? wasmOptPath, string? wasmMergePath)
    {
        var tools = System.Collections.Immutable.ImmutableArray.CreateBuilder<BinaryenNativeTool>();
        if (!string.IsNullOrWhiteSpace(wasmOptPath))
        {
            tools.Add(new(BinaryenToolIds.WasmOpt, wasmOptPath));
        }
        if (!string.IsNullOrWhiteSpace(wasmMergePath))
        {
            tools.Add(new(BinaryenToolIds.WasmMerge, wasmMergePath));
        }
        return tools.ToImmutable();
    }

    public static IHostInteropManifestReader CreateHostInteropManifestReader() =>
        new HostInteropManifestReader();

    public static IComponentManifestWriter CreateComponentManifestWriter() =>
        new ComponentManifestWriter();
    private static ComponentBuildSession CreateComponentBuildSession(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration)
    {
        var services = CreateCompilerServices(wasmToolsCommand, binaryenConfiguration);
        return new ComponentBuildSession(
            services,
            services.GetRequiredService<IComponentBuilder>());
    }

    private static RawModuleLinkSession CreateRawModuleLinkSession(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration)
    {
        var services = CreateCompilerServices(wasmToolsCommand, binaryenConfiguration);
        return new(
            services,
            services.GetRequiredService<IRawModuleLinker>());
    }

    private static RawBindingSession CreateRawBindingSession(
        ExternalToolCommand wasmToolsCommand)
    {
        var services = CreateCompilerServices(wasmToolsCommand);
        return new(
            services,
            services.GetRequiredService<IRawBuildImportSourceValidator>(),
            services.GetRequiredService<IRawAdapterWriter>(),
            services.GetRequiredService<IRawDeploymentFunctionProjector>());
    }

    private static WitFunctionProjectionSession CreateWitFunctionProjectionSession(
        ExternalToolCommand wasmToolsCommand)
    {
        var services = CreateCompilerServices(wasmToolsCommand);
        return new(
            services,
            services.GetRequiredService<IWitDocumentReader>(),
            services.GetRequiredService<NetWasm.Compiler.ComponentModel.Catalogs.IWitWorldFunctionProjector>());
    }

    private static ManagedModuleCompilationSession CreateManagedModuleCompilationSession(
        ExternalToolCommand wasmToolsCommand)
    {
        var services = CreateCompilerServices(wasmToolsCommand);
        var compiler = new ManagedModuleCompiler(
            new ManagedEntryPointReader(),
            new WasmTargetResolver(),
            new NetWasmCompilationInvoker(services.GetRequiredService<INetWasmCompiler>()));
        return new ManagedModuleCompilationSession(services, compiler);
    }

    private static ServiceProvider CreateCompilerServices(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration? binaryenConfiguration = null)
    {
        var services = new ServiceCollection()
            .AddNetWasmCompiler();
        services.RemoveAll<IWasmTools>();
        services.AddSingleton<IWasmTools>(serviceProvider => new ProcessWasmTools(
            serviceProvider.GetRequiredService<IExternalToolRunner>(),
            wasmToolsCommand));
        if (binaryenConfiguration is not null)
        {
            services.RemoveAll<IBinaryenToolRunner>();
            services.AddSingleton<IBinaryenToolRunner>(serviceProvider =>
                new BinaryenToolRunner(
                    binaryenConfiguration,
                    serviceProvider.GetRequiredService<ISystemNodeCommandRunner>(),
                    serviceProvider.GetRequiredService<IExternalToolRunner>()));
        }
        return services.BuildServiceProvider();
    }
}
