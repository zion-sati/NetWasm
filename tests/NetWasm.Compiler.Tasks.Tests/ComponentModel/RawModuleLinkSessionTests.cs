using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class RawModuleLinkSessionTests
{
    [Fact]
    public void SessionDelegatesLinkAndDisposesItsProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposableProbe>();
        var provider = services.BuildServiceProvider();
        var probe = provider.GetRequiredService<DisposableProbe>();
        var linker = new RecordingLinker();
        var session = AsSession(new RawModuleLinkSession(provider, linker));
        var request = new RawModuleLinkRequest(
            "app.wasm",
            "runtime.wasm",
            "linked.wasm",
            ComponentTarget.Wasm32Wasi02);

        session.Link(request);
        session.Dispose();

        Assert.Same(request, linker.Request);
        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public void SessionAndFactoryRejectMissingInputs()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        Assert.Throws<ArgumentNullException>(() =>
            new RawModuleLinkSession(provider, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new RawModuleLinkSessionFactory(null!));
        var factory = AsFactory(new RawModuleLinkSessionFactory(
            (_, _) => throw new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, Binaryen()));
        Assert.Throws<ArgumentNullException>(() => factory.Create(Command(), null!));
    }

    [Fact]
    public void FactoryCreatesConfiguredSingleCapabilitySession()
    {
        var factory = AsFactory(
            CompilerTaskComposition.CreateRawModuleLinkSessionFactory());
        using var session = factory.Create(Command(), Binaryen());

        Assert.IsType<RawModuleLinkSession>(session);
    }

    private static IRawModuleLinkSession AsSession(object session) =>
        (IRawModuleLinkSession)session;

    private static IRawModuleLinkSessionFactory AsFactory(object factory) =>
        (IRawModuleLinkSessionFactory)factory;

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

    private sealed class RecordingLinker : IRawModuleLinker
    {
        public RawModuleLinkRequest? Request { get; private set; }

        public void Link(RawModuleLinkRequest request) => Request = request;
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
