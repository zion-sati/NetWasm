using NetWasm.Hosting.Generator;

namespace NetWasm.Hosting.Generator.Tests;

public sealed class Preview2ShimIdentityReaderTests
{
    [Fact]
    public void ReadsTheExactPinnedShimIdentity()
    {
        var identity = new Preview2ShimIdentityReader().Read(
            """{"name":"@bytecodealliance/preview2-shim","version":"0.24.1"}""");

        Assert.Equal("@bytecodealliance/preview2-shim", identity.Package);
        Assert.Equal("0.24.1", identity.Version);
    }

    [Theory]
    [InlineData("""{"name":"wrong","version":"0.24.1"}""")]
    [InlineData("""{"name":"@bytecodealliance/preview2-shim","version":"0.24.0"}""")]
    [InlineData("""{"name":"@bytecodealliance/preview2-shim"}""")]
    [InlineData("""{"name":1,"version":"0.24.1"}""")]
    [InlineData("not-json")]
    public void RejectsMalformedOrDriftedMetadata(string json)
    {
        Assert.Throws<InvalidDataException>(() =>
            new Preview2ShimIdentityReader().Read(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingMetadata(string json)
    {
        Assert.Throws<ArgumentException>(() =>
            new Preview2ShimIdentityReader().Read(json));
    }
}
