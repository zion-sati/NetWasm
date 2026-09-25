using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusSourceNamesVerifierTests
{
    [Fact]
    public void AcceptsDistinctRelativeCSharpAssetIdentities()
    {
        var verifier = Assert.IsAssignableFrom<ICorpusSourceNamesVerifier>(new CorpusSourceNamesVerifier());

        Assert.Null(Record.Exception(() => verifier.Verify(
            ["Program.cs", "a/Nested.cs.txt", "math-boundary/Helper.Part.cs", "_case/File.cs"])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../Outside.cs")]
    [InlineData("case/../Outside.cs")]
    [InlineData("/absolute/File.cs")]
    [InlineData("C:/File.cs")]
    [InlineData("case\\File.cs")]
    [InlineData("case//File.cs")]
    [InlineData("case./File.cs")]
    [InlineData("case/*.cs")]
    [InlineData("File.cs\n")]
    [InlineData("File.cs\0")]
    [InlineData("File.txt")]
    [InlineData("File.CS")]
    [InlineData("File.cs;command")]
    public void RejectsNonCanonicalOrUnsafeIdentities(string? name)
    {
        var verifier = Assert.IsAssignableFrom<ICorpusSourceNamesVerifier>(new CorpusSourceNamesVerifier());

        Assert.ThrowsAny<ArgumentException>(() => verifier.Verify([name!]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsMissingSourceSets(bool uninitialized)
    {
        var verifier = Assert.IsAssignableFrom<ICorpusSourceNamesVerifier>(new CorpusSourceNamesVerifier());

        Assert.Throws<ArgumentException>(() => verifier.Verify(uninitialized ? default : ImmutableArray<string>.Empty));
    }

    [Theory]
    [InlineData("Case/A.cs", "Case/A.cs")]
    [InlineData("Case/A.cs", "case/a.cs")]
    [InlineData("A.cs", "A.cs/B.cs")]
    [InlineData("a.cs/B.cs", "A.cs")]
    public void RejectsDuplicateAndFileDirectoryCollisionsBeforeMaterialization(string first, string second)
    {
        var verifier = Assert.IsAssignableFrom<ICorpusSourceNamesVerifier>(new CorpusSourceNamesVerifier());

        Assert.Throws<ArgumentException>(() => verifier.Verify([first, second]));
    }
}
