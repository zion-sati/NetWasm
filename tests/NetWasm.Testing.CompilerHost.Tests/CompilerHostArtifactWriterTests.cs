using NetWasm.Testing.CompilerHost;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostArtifactWriterTests
{
    private readonly CompilerHostArtifactWriter _writer = new CompilerHostArtifactWriter();

    [Fact]
    public void WritesExactBytesToEveryRequestedFile()
    {
        var root = Directory.CreateTempSubdirectory("compiler-host-artifacts-").FullName;
        try
        {
            var first = Path.Combine(root, "first.bin");
            var second = Path.Combine(root, "second.bin");

            ((ICompilerHostArtifactWriter)_writer).Write([new(first, [1, 2, 3]), new(second, [])]);

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
            Assert.Empty(File.ReadAllBytes(second));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EmptyBatchDoesNoWorkAndDefaultBatchIsRejected()
    {
        ((ICompilerHostArtifactWriter)_writer).Write([]);
        Assert.Throws<ArgumentException>(() => ((ICompilerHostArtifactWriter)_writer).Write(default));
    }

    [Fact]
    public void WriteFailurePropagatesAndStopsLaterWrites()
    {
        var root = Directory.CreateTempSubdirectory("compiler-host-artifacts-").FullName;
        try
        {
            var missing = Path.Combine(root, "missing", "first.bin");
            var later = Path.Combine(root, "later.bin");

            Assert.Throws<DirectoryNotFoundException>(() =>
                ((ICompilerHostArtifactWriter)_writer).Write([new(missing, [1]), new(later, [2])]));
            Assert.False(File.Exists(later));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
