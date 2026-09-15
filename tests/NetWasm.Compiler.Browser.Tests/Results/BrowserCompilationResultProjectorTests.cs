using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Browser.Tests.Results;

public sealed class BrowserCompilationResultProjectorTests
{
    [Theory]
    [InlineData(CompilerEntryPointKind.RawFunction, null)]
    [InlineData(CompilerEntryPointKind.ManagedExecutable, 0x06000001)]
    public void ProjectsOnlyApplicationAndHostContractsWithExplicitEntryIdentity(
        CompilerEntryPointKind kind, int? token)
    {
        var options = BrowserCompilationRequestTests.CreateOptions() with
        {
            EntryPointKind = kind,
            EntryMethodToken = token,
        };
        var manifest = new HostInteropManifest(1, "wasm32", new(0, 1, 0), new(4, 0, 0, 0, 0), [], []);
        // Reachability and layout payloads are deliberately not needed for projection.
        var source = new CompilationResult([0, 97, 115, 109], null!, null!, manifest, 4096)
        {
            RuntimeFeatures = ["exceptions"],
            FunctionImports = [new WasmFunctionImport("runtime", "function", new([], default))],
        };
        var projector = CreateProjector();

        var actual = projector.Project(source, options);

        Assert.Equal(source.ApplicationModule, actual.ApplicationModule);
        Assert.NotSame(source.ApplicationModule, actual.ApplicationModule);
        Assert.Equal(4096, actual.StaticDataEnd);
        Assert.Equal(source.RuntimeFeatures, actual.RuntimeFeatures);
        Assert.Equal(source.FunctionImports, actual.FunctionImports);
        Assert.Same(manifest, actual.InteropManifest);
        Assert.Equal(new BrowserCompilationEntryPoint("app.dll", "Program", "Main", token, kind), actual.EntryPoint);
        source.ApplicationModule[0] = 9;
        Assert.Equal(0, actual.ApplicationModule[0]);
    }

    [Fact]
    public void RequiresAResultAndOptions()
    {
        var projector = CreateProjector();
        Assert.Throws<ArgumentNullException>(() => projector.Project(null!, BrowserCompilationRequestTests.CreateOptions()));
        Assert.Throws<ArgumentNullException>(() => projector.Project(
            new CompilationResult([], null!, null!, null!, 0), null!));
    }

    private static IBrowserCompilationResultProjector CreateProjector() =>
        Assert.IsAssignableFrom<IBrowserCompilationResultProjector>(new BrowserCompilationResultProjector());
}
