using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Tests;

public sealed class SystemHostPathCanonicalizerTests
{
    [Fact]
    public void CanonicalizeReturnsAnAbsoluteNormalizedPath()
    {
        var canonicalizer = Assert.IsAssignableFrom<IHostPathCanonicalizer>(
            new SystemHostPathCanonicalizer());
        var path = Path.Combine(Path.GetTempPath(), "parent", "..", "tool");

        var result = canonicalizer.Canonicalize(path);

        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tool")), result);
    }

    [Fact]
    public void CanonicalizeRejectsMissingPaths()
    {
        var canonicalizer = Assert.IsAssignableFrom<IHostPathCanonicalizer>(
            new SystemHostPathCanonicalizer());

        Assert.Throws<ArgumentNullException>(() => canonicalizer.Canonicalize(null!));
        Assert.Throws<ArgumentException>(() => canonicalizer.Canonicalize(" "));
    }
}
