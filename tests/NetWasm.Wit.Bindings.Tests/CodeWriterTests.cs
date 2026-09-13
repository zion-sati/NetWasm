using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class CodeWriterTests
{
    [Fact]
    public void WritesCommandsThroughItsContractAndRejectsInvalidCommands()
    {
#pragma warning disable CA1859 // This test verifies the writer's public contract dispatch.
        ICodeWriter writer = new CodeWriter();
#pragma warning restore CA1859

        writer.Write(new(CodeWriterCommandKind.Line, "root"));
        writer.Write(new(CodeWriterCommandKind.Indent));
        writer.Write(new(CodeWriterCommandKind.Line, "child"));
        writer.Write(new(CodeWriterCommandKind.Raw, "tail"));
        writer.Write(new(CodeWriterCommandKind.Unindent));

        Assert.Equal("root\n    child\ntail", writer.Text);
        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(new(CodeWriterCommandKind.Unindent)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.Write(new((CodeWriterCommandKind)99)));
    }
}
