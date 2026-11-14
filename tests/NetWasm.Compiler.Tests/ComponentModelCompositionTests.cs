using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.FileSystem;
using NetWasm.Compiler.ComponentModel.Linking;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Packaging;
using NetWasm.Compiler.ComponentModel.Process;
using NetWasm.Compiler.ComponentModel.WasmTools;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.Tests;

public sealed class ComponentModelCompositionTests
{
    [Fact]
    public void RegistrationComposesEachComponentModelFeature()
    {
        // The host supplies logging and the clock, as AddNetWasmCompiler's diagnostics feature does: the final
        // wasm-opt run reports its size, level and time through them.
        var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System);

        Assert.Same(services, services.AddCompilerComponentModel());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<IFileExistence>());
        Assert.NotNull(provider.GetRequiredService<IProcessExecution>());
        Assert.NotNull(provider.GetRequiredService<IExternalToolRunner>());
        Assert.NotNull(provider.GetRequiredService<IConfiguredExternalToolRunner>());
        Assert.NotNull(provider.GetRequiredService<ISystemNodeCommandRunner>());
        Assert.NotNull(provider.GetRequiredService<IBinaryenToolRunner>());
        Assert.NotNull(provider.GetRequiredService<IWasmTools>());
        Assert.NotNull(provider.GetRequiredService<IComponentCoreModuleLinker>());
        Assert.NotNull(provider.GetRequiredService<IComponentPackager>());
        Assert.NotNull(provider.GetRequiredService<IWitInterfaceSpecifierFormatter>());
        Assert.NotNull(provider.GetRequiredService<IWitTypeIdentityFormatter>());
        Assert.NotNull(provider.GetRequiredService<IWitInterfaceCatalogBuilder>());
        Assert.NotNull(provider.GetRequiredService<IWitWorldFunctionProjector>());
        Assert.NotNull(provider.GetRequiredService<IWitDocumentJsonReader>());
    }

    [Fact]
    public void FeatureRegistrationsRejectNullServiceCollections()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerComponentModel());
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerComponentModelFileSystem());
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerComponentModelProcess());
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerComponentModelWasmTools());
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerComponentModelLinking());
        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerComponentModelPackaging());
    }
}
