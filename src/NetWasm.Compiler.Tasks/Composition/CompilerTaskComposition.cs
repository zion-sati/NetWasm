using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Composition;

internal static class CompilerTaskComposition
{
    public static IInternalRuntimeExportReader CreateInternalRuntimeExportReader() => new InternalRuntimeExportReader();

    public static ICompilerArtifactManifestTaskRequestBuilder CreateArtifactManifestTaskRequestBuilder() =>
        new CompilerArtifactManifestTaskRequestBuilder();

    public static ICompilerArtifactManifestBuilder CreateArtifactManifestBuilder() =>
        new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator());

    public static ICompilerArtifactManifestWriter CreateArtifactManifestWriter() =>
        new CompilerArtifactManifestWriter();

    public static ICompilerArtifactManifestReader CreateArtifactManifestReader() =>
        new CompilerArtifactManifestReader();

    public static ICompilerArtifactManifestValidator CreateArtifactManifestValidator() =>
        new CompilerArtifactManifestValidator(
            CreateArtifactManifestBuilder(),
            CreateArtifactManifestReader());

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

    public static IWitWorkerContractWriter CreateWitWorkerContractWriter() =>
        new WitWorkerContractWriter(CreateByteArtifactWriter());

    public static IComponentManifestInputsReader CreateComponentManifestInputsReader() =>
        new ComponentManifestInputsReader(CreateHostInteropManifestReader());

    public static IComponentWitWorldSelector CreateComponentWitWorldSelector() =>
        new ComponentWitWorldSelector();

    public static ICompilerBuildMetadataReader CreateCompilerBuildMetadataReader() =>
        new CompilerBuildMetadataReader();

    public static ExternalToolCommand CreateWasmToolsCommand(
        string nodePath,
        string commandPath,
        string modulePath,
        string? nativePath = null) => ExternalToolCommand.ForWasmTools(nodePath, commandPath, modulePath, nativePath);

    public static BinaryenToolRunnerConfiguration CreateBinaryenConfiguration(
        string nodePath,
        string wasmOptPath,
        string wasmMergePath,
        string? nativeWasmOptPath = null) => new(
            nodePath,
            [
                new(BinaryenToolIds.WasmOpt, wasmOptPath),
                new(BinaryenToolIds.WasmMerge, wasmMergePath),
            ],
            string.IsNullOrWhiteSpace(nativeWasmOptPath)
                ? []
                : [new(BinaryenToolIds.WasmOpt, nativeWasmOptPath)]);

    public static IHostInteropManifestReader CreateHostInteropManifestReader() =>
        new HostInteropManifestReader();

    public static IComponentManifestWriter CreateComponentManifestWriter() =>
        new ComponentManifestWriter();
    private static ComponentBuildSession CreateComponentBuildSession(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration,
        ILoggerProvider progress)
    {
        var services = CreateCompilerServices(wasmToolsCommand, binaryenConfiguration, progress);
        return new ComponentBuildSession(
            services,
            services.GetRequiredService<IComponentBuilder>());
    }

    private static RawModuleLinkSession CreateRawModuleLinkSession(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration binaryenConfiguration,
        ILoggerProvider progress)
    {
        var services = CreateCompilerServices(wasmToolsCommand, binaryenConfiguration, progress);
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

    private static ServiceProvider CreateCompilerServices(
        ExternalToolCommand wasmToolsCommand,
        BinaryenToolRunnerConfiguration? binaryenConfiguration = null,
        ILoggerProvider? progress = null)
    {
        var services = new ServiceCollection()
            .AddNetWasmCompiler();
        if (progress is not null)
        {
            services.AddLogging(logging => logging
                .AddProvider(progress)
                .AddFilter<MsBuildProgressLoggerProvider>(MsBuildProgressLoggerProvider.Shows));
        }
        services.RemoveAll<IWasmTools>();
        services.AddSingleton<IWasmTools>(serviceProvider =>
        {
            var tools = new ProcessWasmTools(
                serviceProvider.GetRequiredService<IExternalToolRunner>(),
                wasmToolsCommand);
            return CachingWasmTools.DefaultDirectory() is { } cache
                ? new CachingWasmTools(tools, wasmToolsCommand, cache)
                : tools;
        });
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
