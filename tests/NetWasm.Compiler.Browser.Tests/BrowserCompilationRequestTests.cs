namespace NetWasm.Compiler.Browser.Tests;

public sealed class BrowserCompilationRequestTests
{
    [Fact]
    public void SnapshotsCallerMapsAndByteArraysWithExactOrdinalPaths()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var inputs = new Dictionary<string, byte[]> { ["contracts/compiler.wit.wasm"] = bytes };
        var documents = new Dictionary<string, string> { ["contracts/compiler.wit.wasm"] = "original JSON" };
        var options = CreateOptions();

        var request = new BrowserCompilationRequest(options, inputs, documents);
        bytes[0] = 9;
        inputs.Clear();
        documents["contracts/compiler.wit.wasm"] = "changed JSON";

        Assert.Same(options, request.Options);
        Assert.False(request.SelectManagedExecutableEntryPoint);
        Assert.False(request.CollectCompilerMetrics);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Inputs["contracts/compiler.wit.wasm"]);
        Assert.Equal("original JSON", request.NormalizedWitDocuments["contracts/compiler.wit.wasm"]);
        Assert.False(request.Inputs.ContainsKey("compiler.wit.wasm"));
        Assert.False(request.Inputs.ContainsKey("contracts/COMPILER.wit.wasm"));
    }

    [Fact]
    public void CompilerMetricsAreExplicitlyOptIn()
    {
        var request = new BrowserCompilationRequest(
            CreateOptions(),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, string>())
        {
            CollectCompilerMetrics = true,
        };

        Assert.True(request.CollectCompilerMetrics);
    }

    [Fact]
    public void RequiresOriginalWitBytesForEveryNormalizedDocument()
    {
        var failure = Assert.Throws<FileNotFoundException>(() => new BrowserCompilationRequest(
            CreateOptions(), new Dictionary<string, byte[]>(),
            new Dictionary<string, string> { ["contract.wit.wasm"] = "JSON" }));

        Assert.Equal("contract.wit.wasm", failure.FileName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsFilesystemDiagnosticOutputOptions(bool trace)
    {
        var options = trace
            ? CreateOptions() with { DiagnosticTracePath = "trace.json" }
            : CreateOptions() with { DiagnosticLogPath = "compiler.log" };

        var failure = Assert.Throws<ArgumentException>(() => new BrowserCompilationRequest(
            options, new Dictionary<string, byte[]>(), new Dictionary<string, string>()));

        Assert.Equal("options", failure.ParamName);
    }

    [Fact]
    public void RejectsMissingArgumentsAndInvalidVirtualValues()
    {
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilationRequest(
            null!, new Dictionary<string, byte[]>(), new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilationRequest(
            CreateOptions(), null!, new Dictionary<string, string>()));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilationRequest(
            CreateOptions(), new Dictionary<string, byte[]>(), null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilationRequest(
            CreateOptions(), new Dictionary<string, byte[]> { ["app.dll"] = null! },
            new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new BrowserCompilationRequest(
            CreateOptions(), new Dictionary<string, byte[]> { [" "] = [] },
            new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new BrowserCompilationRequest(
            CreateOptions(), new Dictionary<string, byte[]> { ["contract.wit"] = [] },
            new Dictionary<string, string> { ["contract.wit"] = " " }));
    }

    [Fact]
    public void ExplicitPeSelectionRequiresManagedExecutableOptions()
    {
        Assert.Throws<ArgumentException>(() => new BrowserCompilationRequest(CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>(),
            selectManagedExecutableEntryPoint: true));
        var request = new BrowserCompilationRequest(
            CreateOptions() with { EntryPointKind = CompilerEntryPointKind.ManagedExecutable },
            new Dictionary<string, byte[]>(), new Dictionary<string, string>(),
            selectManagedExecutableEntryPoint: true);
        Assert.True(request.SelectManagedExecutableEntryPoint);
    }

    internal static CompilerOptions CreateOptions() => new("app.dll", [], "Program", "Main", []);
}
