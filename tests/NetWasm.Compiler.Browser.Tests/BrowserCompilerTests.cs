namespace NetWasm.Compiler.Browser.Tests;

public sealed class BrowserCompilerTests
{
    [Fact]
    public void RequiresARequest()
    {
        Assert.Throws<ArgumentNullException>(() => BrowserCompiler.Compile(null!));
    }

    [Fact]
    public void TheComposedCompilerFailsClosedOnAnUnsuppliedEntryAssembly()
    {
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());

        var failure = Assert.Throws<FileNotFoundException>(() => BrowserCompiler.Compile(request));

        Assert.Equal("app.dll", failure.FileName);
    }
}
