// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
            "e762ea609dd87f4de8948f6d285027db8124cf5bf6fac449db44a6e6bc276b85",
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
