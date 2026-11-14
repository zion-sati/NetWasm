using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

// The parts a runtime tool's command is made of: the excerpt a failure message quotes, the quoting wasm-ld's tokenizer
// reads back, the response file and the linker command that names it.
public sealed class ToolCommandPartsTests
{
    [Fact]
    public void TheExcerptIsTheLastTwentyLines()
    {
        var formatter = new ToolOutputExcerptFormatter();
        var many = string.Join("\r\n", Enumerable.Range(1, 25).Select(line => $"line {line}")) + "\r\n";

        Assert.Equal("", formatter.Format(""));
        Assert.Equal("", formatter.Format("\n\n"));
        Assert.Equal(" Its last output:\none\ntwo", formatter.Format("one\ntwo\n"));
        Assert.Equal(" Its last output:\n" + string.Join('\n', Enumerable.Range(6, 20).Select(line => $"line {line}")), formatter.Format(many));
        Assert.Throws<ArgumentNullException>(() => formatter.Format(null!));
        Assert.Throws<ArgumentNullException>(() => new CommandInvoker(null!));
    }

    // LLVM's GNU tokenizer takes a backslash inside quotes as an escape, so backslashes and single quotes are escaped.
    [Fact]
    public void ArgumentsAreQuotedForLlvmsTokenizer()
    {
        var quoter = new LlvmResponseFileArgumentQuoter();

        Assert.Equal("''", quoter.Quote(""));
        Assert.Equal("'--undefined=run'", quoter.Quote("--undefined=run"));
        Assert.Equal(@"'C:\\with space\\it\'s.o'", quoter.Quote(@"C:\with space\it's.o"));
        Assert.Throws<ArgumentNullException>(() => quoter.Quote(null!));
    }

    [Fact]
    public void TheResponseFileHoldsOneQuotedArgumentPerLine()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathTo("nested/link.rsp");
        var writer = new ResponseFileWriter(new UpperCaseQuoter());

        writer.Write(path, ["a", "b"]);

        Assert.Equal(["A", "B"], File.ReadAllLines(path));
        Assert.Throws<ArgumentException>(() => writer.Write("", ["a"]));
        Assert.Throws<ArgumentNullException>(() => new ResponseFileWriter(null!));
    }

    // The linker reads its arguments from a response file next to its log, with the tokenizer the quoting is for.
    [Fact]
    public void TheLinkerCommandNamesItsResponseFile()
    {
        using var directory = new TemporaryDirectory();
        var log = directory.PathTo("logs/link.log");
        var writer = new RecordingWriter();
        var factory = Assert.IsAssignableFrom<ILinkerCommandFactory>(new ResponseFileLinkerCommandFactory(writer));

        var command = factory.Create("wasm-ld", ["--undefined=run"], log);

        var responseFile = Path.ChangeExtension(Path.GetFullPath(log), ".rsp");
        Assert.Equal(responseFile, writer.Path);
        Assert.Equal(["--undefined=run"], writer.Arguments);
        Assert.Equal("wasm-ld", command.ExecutablePath);
        Assert.Equal(log, command.LogPath);
        Assert.Equal(["--rsp-quoting=posix", "@" + responseFile], command.Arguments);
        Assert.Throws<ArgumentNullException>(() => new ResponseFileLinkerCommandFactory(null!));
    }

    private sealed class UpperCaseQuoter : IResponseFileArgumentQuoter
    {
        public string Quote(string argument) => argument.ToUpperInvariant();
    }

    private sealed class RecordingWriter : IResponseFileWriter
    {
        public string? Path { get; private set; }
        public ImmutableArray<string> Arguments { get; private set; } = [];

        public void Write(string path, ImmutableArray<string> arguments) => (Path, Arguments) = (path, arguments);
    }
}
