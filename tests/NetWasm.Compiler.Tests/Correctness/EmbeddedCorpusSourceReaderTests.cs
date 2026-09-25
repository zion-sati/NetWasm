using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class EmbeddedCorpusSourceReaderTests
{
    private readonly ICorpusSourceReader _reader =
        Assert.IsAssignableFrom<ICorpusSourceReader>(new EmbeddedCorpusSourceReader());

    [Fact]
    public void ReadsTheExactVersionedSourceWithoutPathDependentPreprocessing()
    {
        var source = _reader.Read("math-boundary/Program.cs.txt");

        Assert.Equal(
            "41863545be951f4a244105f9440f3f7e21d6a8851c38587e7d80c2b3a0fefa74",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        Assert.Equal(source, _reader.Read("math-boundary/Program.cs.txt"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void RejectsAnEmptyResourceIdentity(string? sourceFile) =>
        Assert.ThrowsAny<ArgumentException>(() => _reader.Read(sourceFile!));

    [Theory]
    [InlineData("missing/Program.cs.txt")]
    [InlineData("Math-boundary/Program.cs.txt")]
    [InlineData("math-boundary\\Program.cs.txt")]
    [InlineData("../math-boundary/Program.cs.txt")]
    [InlineData("/math-boundary/Program.cs.txt")]
    public void MissingOrNonExactIdentityCannotFallBackToAnotherSource(string sourceFile)
    {
        var error = Assert.Throws<FileNotFoundException>(() => _reader.Read(sourceFile));

        Assert.Equal(sourceFile, error.FileName);
    }
}
