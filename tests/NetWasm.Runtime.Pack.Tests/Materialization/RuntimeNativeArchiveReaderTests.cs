using System.Security.Cryptography;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeArchiveReaderTests
{
    [Fact]
    public void ReadsRegularArchiveAndHashesTheCompleteCurrentFile()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.PathTo("libmule.a");
        var first = "!<arch>\nfirst content"u8.ToArray();
        File.WriteAllBytes(path, first);
        var reader = Assert.IsAssignableFrom<IRuntimeNativeArchiveReader>(new RuntimeNativeArchiveReader());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant(), reader.Read(path));
        var next = "!<arch>\nchanged content"u8.ToArray();
        File.WriteAllBytes(path, next);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(next)).ToLowerInvariant(), reader.Read(path));
    }

    [Theory]
    [InlineData("!<thin>\nmember")]
    [InlineData("dynamic container")]
    [InlineData("\0asm1234")]
    public void RejectsNonRegularContainers(string contents)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("libmule.a", contents);
        var error = Assert.Throws<InvalidOperationException>(() => new RuntimeNativeArchiveReader().Read(path));
        Assert.Contains("must be regular archives", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!<arch")]
    public void RejectsTruncatedFiles(string contents)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("libmule.a", contents);
        var error = Assert.Throws<InvalidOperationException>(() => new RuntimeNativeArchiveReader().Read(path));
        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
        Assert.IsType<EndOfStreamException>(error.InnerException);
    }

    [Fact]
    public void RejectsMissingArchiveAndInvalidPathContract()
    {
        using var directory = new TemporaryDirectory();
        var reader = Assert.IsAssignableFrom<IRuntimeNativeArchiveReader>(new RuntimeNativeArchiveReader());
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
        Assert.Throws<InvalidOperationException>(() => reader.Read("relative.a"));
        Assert.Throws<InvalidOperationException>(() => reader.Read(directory.PathTo("native.o")));
        var error = Assert.Throws<InvalidOperationException>(() => reader.Read(directory.PathTo("missing.a")));
        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
        Assert.IsType<FileNotFoundException>(error.InnerException);
    }
}
