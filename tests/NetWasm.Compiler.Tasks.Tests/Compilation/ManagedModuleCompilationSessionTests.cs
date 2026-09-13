using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Composition;
using NetWasm.Compiler.Tasks.Tests.TestSupport;

namespace NetWasm.Compiler.Tasks.Tests.Compilation;

public sealed class ManagedModuleCompilationSessionTests
{
    [Fact]
    public void SessionDelegatesCompilationAndDisposesItsProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisposableProbe>();
        var provider = services.BuildServiceProvider();
        var probe = provider.GetRequiredService<DisposableProbe>();
        var compiler = new RecordingCompiler();
        var session = AsSession(new ManagedModuleCompilationSession(provider, compiler));
        var request = Request();

        var result = session.Compile(request);
        session.Dispose();

        Assert.Same(compiler.Result, result);
        Assert.Same(request, compiler.Request);
        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public void SessionAndFactoryRejectMissingInputs()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedModuleCompilationSession(null!, new RecordingCompiler()));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedModuleCompilationSession(provider, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedModuleCompilationSessionFactory(null!));
        var factory = AsFactory(new ManagedModuleCompilationSessionFactory(
            _ => throw new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }

    [Fact]
    public void FactoryCreatesConfiguredCompilationSession()
    {
        var factory = AsFactory(
            CompilerTaskComposition.CreateManagedModuleCompilationSessionFactory());

        using var session = factory.Create(Command());

        Assert.IsType<ManagedModuleCompilationSession>(session);
    }

    private static IManagedModuleCompilationSession AsSession(object session) =>
        (IManagedModuleCompilationSession)session;

    private static IManagedModuleCompilationSessionFactory AsFactory(object factory) =>
        (IManagedModuleCompilationSessionFactory)factory;

    private static ExternalToolCommand Command() => new(
        "node",
        ["run-wasm-tools.mjs", "wasm-tools.wasm"]);

    private static ManagedModuleCompileRequest Request() => new(
        "application.dll",
        [],
        [],
        "wasm32",
        null,
        null,
        false,
        "command.wit",
        "command");

    private sealed class RecordingCompiler : IManagedModuleCompiler
    {
        public ManagedModuleCompilation Result { get; } =
            CompilerTaskTestData.CreateCompilation();
        public ManagedModuleCompileRequest? Request { get; private set; }

        public ManagedModuleCompilation Compile(ManagedModuleCompileRequest request)
        {
            Request = request;
            return Result;
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
