namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusReplayCommandFormatterTests
{
    [Theory]
    [InlineData("Example.Tests.ReplaysCase")]
    [InlineData("_Tests.Case2._Runs")]
    public void FormatUsesExactMethodFilterAndExplicitProject(string method)
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Equal(
            "dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj " +
            $"-c Release --filter 'FullyQualifiedName={method}'",
            formatter.Format(method));
    }

    [Fact]
    public void FormatDoesNotInventACommandWhenIdentityIsAbsent()
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Null(formatter.Format(null));
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("Emitted-Wasm64-Optimized", null, "&DisplayName~Emitted-Wasm64-Optimized&DisplayName!~Emitted-Wasm64-Optimized-")]
    [InlineData(null, -3, "&DisplayName~input: -3,")]
    [InlineData("Release-Wasm32-Direct", 2, "&DisplayName~Release-Wasm32-Direct&DisplayName!~Release-Wasm32-Direct-&DisplayName~input: 2,")]
    [InlineData("Release-Wasm32-Direct-Linked", null, "&DisplayName~Release-Wasm32-Direct-Linked&DisplayName!~Release-Wasm32-Direct-Linked-")]
    [InlineData("Debug-Wasm64-Optimized-Linked", 2, "&DisplayName~Debug-Wasm64-Optimized-Linked&DisplayName!~Debug-Wasm64-Optimized-Linked-&DisplayName~input: 2,")]
    public void FormatIncludesOnlyExplicitReplaySelection(string? cell, int? input, string suffix)
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Equal("dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj " +
            $"-c Release --filter 'FullyQualifiedName=Example.Tests.Run{suffix}'",
            formatter.Format("Example.Tests.Run", cell, input));
    }

    [Theory]
    [InlineData("Emitted-Wasm32-Direct", null)]
    [InlineData(null, 0)]
    public void FormatDoesNotInventACommandForSelectedCasesWithoutOwningMethod(string? cell, int? input)
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Null(formatter.Format(null, cell, input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Emitted-Wasm128-Direct")]
    [InlineData("Release-Wasm32-Direct-Simulated")]
    [InlineData("Release-Wasm32-Direct-Linked-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked\n")]
    [InlineData("Release-Wasm32-Direct\n")]
    [InlineData("Release-Wasm32-Direct|DisplayName~Other")]
    [InlineData("Release-Wasm32-Direct'; echo injected")]
    public void FormatRejectsMalformedCellFilters(string cell)
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Equal("cellId", Assert.Throws<ArgumentException>(() => formatter.Format("Example.Tests.Run", cell)).ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("FixtureName")]
    [InlineData("Example..Run")]
    [InlineData("Example.1Run")]
    [InlineData("Example.Run ")]
    [InlineData("Example.Run\n")]
    [InlineData("Example.Run|FullyQualifiedName~Other")]
    [InlineData("Example.Run' ; echo injected")]
    [InlineData("Example.$(command)")]
    [InlineData("Example.`command`")]
    public void FormatRejectsMalformedOrExecutableIdentities(string method)
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        var exception = Assert.Throws<ArgumentException>(() => formatter.Format(method));

        Assert.Equal("testMethod", exception.ParamName);
    }

    [Theory]
    [InlineData(0, "Fast")]
    [InlineData(1, "Family")]
    [InlineData(2, "Extended")]
    public void ReplayPinsTheProfileInsteadOfDependingOnTheManifestDefault(int profile, string name)
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Equal("dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj " +
            $"-c Release --environment NETWASM_CORPUS_PROFILE={name} --filter 'FullyQualifiedName=Example.Tests.Run&DisplayName~Emitted-Wasm32-Direct&DisplayName!~Emitted-Wasm32-Direct-&DisplayName~input: 2,'",
            formatter.Format("Example.Tests.Run", "Emitted-Wasm32-Direct", 2, (CorpusMatrixProfile)profile));
    }

    [Fact]
    public void InvalidProfileCannotBecomeAnEnvironmentAssignment()
    {
        var formatter = Assert.IsAssignableFrom<ICorpusReplayCommandFormatter>(new CorpusReplayCommandFormatter());

        Assert.Equal("profile", Assert.Throws<ArgumentOutOfRangeException>(() =>
            formatter.Format("Example.Tests.Run", profile: (CorpusMatrixProfile)99)).ParamName);
        Assert.Null(formatter.Format(null, profile: (CorpusMatrixProfile)99));
    }
}
