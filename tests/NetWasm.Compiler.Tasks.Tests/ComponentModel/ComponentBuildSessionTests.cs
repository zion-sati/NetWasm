using NetWasm.Compiler.Tasks.Composition;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class ComponentBuildSessionTests
{
    [Fact]
    public void SessionDelegatesBuildAndDisposesItsProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposableProbe>();
        var provider = services.BuildServiceProvider();
        var probe = provider.GetRequiredService<DisposableProbe>();
        var components = new RecordingBuilder();
        var session = AsSession(new ComponentBuildSession(provider, components));
        var request = CreateRequest();

        var result = session.Build(request);
        session.Dispose();

        Assert.Same(components.Manifest, result);
        Assert.Same(request, components.Request);
        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public void SessionAndFactoryRejectMissingInputs()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuildSession(provider, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuildSessionFactory(null!));
        var factory = AsFactory(new ComponentBuildSessionFactory(
            (_, _) => throw new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, Binaryen()));
        Assert.Throws<ArgumentNullException>(() => factory.Create(Command(), null!));
    }

    [Fact]
    public void FactoryCreatesConfiguredSingleCapabilitySession()
    {
        var factory = AsFactory(CompilerTaskComposition.CreateComponentBuildSessionFactory());
        using var session = factory.Create(Command(), Binaryen());

        Assert.IsType<ComponentBuildSession>(session);
    }

    private static ComponentBuildRequest CreateRequest() => new(
        "app.wasm",
        "runtime.wasm",
        "command.wit",
        "command",
        "component.wasm",
        ComponentTarget.Wasm32Wasi02,
        new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None));

    private static IComponentBuildSession AsSession(object session) =>
        (IComponentBuildSession)session;

    private static IComponentBuildSessionFactory AsFactory(object factory) =>
        (IComponentBuildSessionFactory)factory;

    private static ExternalToolCommand Command() => new(
        "node",
        ["run-wasm-tools.mjs", "wasm-tools.wasm"]);

    private static BinaryenToolRunnerConfiguration Binaryen() => new(
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "node")),
        [
            new(BinaryenToolIds.WasmOpt,
                Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wasm-opt"))),
            new(BinaryenToolIds.WasmMerge,
                Path.GetFullPath(Path.Combine(Path.GetTempPath(), "wasm-merge"))),
        ]);

    private sealed class RecordingBuilder : IComponentBuilder
    {
        public ComponentManifest Manifest { get; } = new(
            1, "package", "command", "wasm32", "0.2", "utf8", "hash", "tools",
            [], [], [], []);

        public ComponentBuildRequest? Request { get; private set; }

        public ComponentManifest Build(ComponentBuildRequest request)
        {
            Request = request;
            return Manifest;
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
