using NetWasm.Compiler.Cli.Tests.TestSupport;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class NwcArgumentReaderTests
{
    private static readonly HashSet<string> Known = ["out", "r", "jsexportboundary"];

    [Fact]
    public void ReadsEveryCscOptionForm()
    {
        var read = NwcArgumentReader.Read(
            ["-out:a.wasm", "/OUT:b.wasm", "-R:C:/lib/x.dll", "-jsexportboundary+", "-JsExportBoundary-",
                "-jsexportboundary", "App.dll", "/abs/path/Other.dll", "-"],
            Known,
            new InMemoryTextFiles());

        Assert.Equal(
            [
                new NwcOption("out", "a.wasm", "-out:a.wasm"),
                new NwcOption("out", "b.wasm", "/OUT:b.wasm"),
                new NwcOption("r", "C:/lib/x.dll", "-R:C:/lib/x.dll"),
                new NwcOption("jsexportboundary", "+", "-jsexportboundary+"),
                new NwcOption("jsexportboundary", "-", "-JsExportBoundary-"),
                new NwcOption("jsexportboundary", null, "-jsexportboundary"),
            ],
            read.Options);
        Assert.Equal(["App.dll", "/abs/path/Other.dll", "-"], read.Inputs);
        Assert.Empty(read.Errors);
    }

    [Fact]
    public void EveryCommandTakesTheCommonOptions()
    {
        var read = NwcArgumentReader.Read(["-nologo", "/help", "-?"], new HashSet<string>(), new InMemoryTextFiles());

        Assert.Equal(["nologo", "help", "?"], read.Options.Select(option => option.Name));
        Assert.Empty(read.Errors);
    }

    [Fact]
    public void ReportsEveryUnrecognizedOptionAndKeepsReading()
    {
        var read = NwcArgumentReader.Read(
            ["-unknown", "App.dll", "-also:1", "-+", "-out:a.wasm"],
            Known,
            new InMemoryTextFiles());

        Assert.Equal(
            ["unrecognized option '-unknown'", "unrecognized option '-also:1'", "unrecognized option '-+'"],
            read.Errors);
        Assert.Equal(["App.dll"], read.Inputs);
        Assert.Equal("a.wasm", Assert.Single(read.Options).Value);
    }

    [Fact]
    public void ExpandsResponseFilesInPlace()
    {
        var files = new InMemoryTextFiles(new()
        {
            ["build.rsp"] = """
                # A comment line, then a blank one

                -out:"bin/my app.wasm"  "My App.dll"
                  -r:first.dll ""
                """.Replace("\n", "\r\n", StringComparison.Ordinal),
        });

        var read = NwcArgumentReader.Read(["-jsexportboundary", "@build.rsp", "-r:last.dll"], Known, files);

        Assert.Equal(["build.rsp"], files.Reads);
        Assert.Equal(
            ["-jsexportboundary", "-out:\"bin/my app.wasm\"", "-r:first.dll", "-r:last.dll"],
            read.Options.Select(option => option.Spelling));
        Assert.Equal("bin/my app.wasm", new NwcOptionValues(read).Single("out"));
        Assert.Equal(["My App.dll", ""], read.Inputs);
        Assert.Empty(read.Errors);
    }

    [Theory]
    [InlineData(@"-out:C:\obj\app.wasm", @"C:\obj\app.wasm")]
    [InlineData(@"-out:""C:\my obj\\""", @"C:\my obj\")]
    [InlineData(@"-out:""say \""hi\"".wasm""", @"say ""hi"".wasm")]
    [InlineData(@"-out:a\\\""b", @"a\""b")]
    [InlineData(@"-out:trailing\", @"trailing\")]
    public void ReadsBackslashesAndQuotesAsCscDoes(string line, string value)
    {
        var files = new InMemoryTextFiles(new() { ["build.rsp"] = line });

        var read = NwcArgumentReader.Read(["@build.rsp"], Known, files);

        Assert.Empty(read.Errors);
        Assert.Equal(value, new NwcOptionValues(read).Single("out"));
    }

    [Fact]
    public void AQuotedSeparatorStaysInsideOneReference()
    {
        var files = new InMemoryTextFiles(new()
        {
            ["build.rsp"] = """
                -r:"lib/NetWasm,Version=v0.1/A.dll";b.dll,"c d.dll"
                -r:"say \"hi\";.dll",,e.dll
                """,
        });

        var read = NwcArgumentReader.Read(["@build.rsp", "-r:f.dll;g.dll"], Known, files);

        Assert.Equal(
            ["lib/NetWasm,Version=v0.1/A.dll", "b.dll", "c d.dll", "say \"hi\";.dll", "e.dll", "f.dll", "g.dll"],
            new NwcOptionValues(read).List("r"));
    }

    [Fact]
    public void ReportsResponseFileFaultsAndReadsTheRest()
    {
        var files = new InMemoryTextFiles(new()
        {
            ["outer.rsp"] = "@inner.rsp\n-out:\"unterminated\n-r:kept.dll\n",
        });

        var read = NwcArgumentReader.Read(["@", "@missing.rsp", "@outer.rsp"], Known, files);

        Assert.Equal(
            [
                "'@' must name a response file",
                "cannot read response file 'missing.rsp': Could not find file 'missing.rsp'.",
                "a response file cannot name another response file: '@inner.rsp'",
                "a response file line has an unterminated quote: -out:\"unterminated",
            ],
            read.Errors);
        Assert.Equal("kept.dll", Assert.Single(read.Options).Value);
        Assert.Equal(["missing.rsp", "outer.rsp"], files.Reads);
    }

    [Fact]
    public void ReportsAResponseFileItCannotAccess()
    {
        var read = NwcArgumentReader.Read(
            ["@locked.rsp"],
            Known,
            new ThrowingTextFiles(new UnauthorizedAccessException("Access denied.")));

        Assert.Equal("cannot read response file 'locked.rsp': Access denied.", Assert.Single(read.Errors));
    }

    [Fact]
    public void PropagatesAnyOtherResponseFileFailure()
    {
        var failure = new InvalidOperationException("reader failure");

        var actual = Assert.Throws<InvalidOperationException>(() =>
            NwcArgumentReader.Read(["@build.rsp"], Known, new ThrowingTextFiles(failure)));

        Assert.Same(failure, actual);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var files = new InMemoryTextFiles();

        Assert.Throws<ArgumentNullException>(() => NwcArgumentReader.Read(null!, Known, files));
        Assert.Throws<ArgumentNullException>(() => NwcArgumentReader.Read([], null!, files));
        Assert.Throws<ArgumentNullException>(() => NwcArgumentReader.Read([], Known, null!));
    }

    private sealed class ThrowingTextFiles(Exception failure) : ITextFileReader
    {
        public string Read(string path) => throw failure;
    }
}
