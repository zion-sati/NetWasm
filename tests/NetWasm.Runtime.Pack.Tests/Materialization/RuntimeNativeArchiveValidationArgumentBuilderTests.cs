using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeArchiveValidationArgumentBuilderTests
{
    [Theory]
    [InlineData("wasm32", "-mwasm32")]
    [InlineData("wasm64", "-mwasm64")]
    public void ForcesEveryArchiveMemberThroughRelocatableTargetValidation(string target, string machine)
    {
        var archive = Path.GetFullPath(Path.Combine("native", target, "libexample.a"));
        var output = Path.GetFullPath(Path.Combine("validation", target, "libexample.o"));
        var builder = Assert.IsAssignableFrom<IRuntimeNativeArchiveValidationArgumentBuilder>(
            new RuntimeNativeArchiveValidationArgumentBuilder());

        Assert.Equal([machine, "-r", "--allow-multiple-definition", "--whole-archive", archive, "--no-whole-archive",
            "--allow-undefined", "-o", output], builder.Build(new(target, archive, output)).ToArray());
    }

    [Theory]
    [InlineData("unknown", "archive.a", "output.o", false)]
    [InlineData("wasm32", "", "output.o", true)]
    [InlineData("wasm32", "archive.a", "", true)]
    [InlineData("wasm32", "relative.a", "/absolute.o", false)]
    [InlineData("wasm32", "/absolute.a", "relative.o", false)]
    [InlineData("wasm32", "/archive\n.a", "/output.o", false)]
    [InlineData("wasm32", "/archive.a", "/output\n.o", false)]
    public void RejectsUnsupportedOrAmbiguousRequests(
        string target, string archive, string output, bool argumentFailure)
    {
        var request = new RuntimeNativeArchiveValidationRequest(target, archive, output);
        if (argumentFailure)
            Assert.Throws<ArgumentException>(() => new RuntimeNativeArchiveValidationArgumentBuilder().Build(request));
        else
            Assert.Throws<InvalidOperationException>(() => new RuntimeNativeArchiveValidationArgumentBuilder().Build(request));
    }

    [Fact]
    public void RejectsInputOverwriteAndNullRequest()
    {
        var path = Path.GetFullPath("archive.a");
        var builder = new RuntimeNativeArchiveValidationArgumentBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build(new("wasm32", path, path)));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
    }
}
