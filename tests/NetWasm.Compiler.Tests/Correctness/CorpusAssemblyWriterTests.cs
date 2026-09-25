using System.Collections.Immutable;
using System.Security.Cryptography;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusAssemblyWriterTests
{
    [Fact]
    public void WritesTheExactImageOnceWithoutFabricatingSourceOrSymbols()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-emitted-writer-");
        try
        {
            var directories = new RecordingDirectories(directory.FullName);
            var subject = Assert.IsAssignableFrom<ICorpusAssemblyWriter>(new CorpusAssemblyWriter(directories));
            ImmutableArray<byte> image = [1, 2, 3];

            var result = subject.Write(image);

            Assert.Equal(1, directories.Calls);
            Assert.Equal(directory.FullName, result.Directory);
            Assert.Equal(Path.Combine(directory.FullName, "fixture.dll"), result.Artifact.AssemblyPath);
            Assert.Equal(image, File.ReadAllBytes(result.Artifact.AssemblyPath));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(image.AsSpan())), result.Artifact.AssemblySha256);
            Assert.Empty(result.Artifact.PdbPath);
            Assert.Empty(result.Artifact.PdbSha256);
            Assert.Equal("not-applicable", result.Artifact.CompilerVersion);
            Assert.Equal<string>(["input-kind:emitted", $"producer-runtime:{Environment.Version}"], result.Artifact.CompilerOptions);
            Assert.Single(Directory.EnumerateFileSystemEntries(directory.FullName));

            Assert.Throws<IOException>(() => subject.Write([9]));
            Assert.Equal(image, File.ReadAllBytes(result.Artifact.AssemblyPath));
            Assert.Equal(2, directories.Calls);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingImagesFailBeforeDirectoryAllocation(bool useDefault)
    {
        var directories = new RecordingDirectories("unused");
        var subject = Assert.IsAssignableFrom<ICorpusAssemblyWriter>(new CorpusAssemblyWriter(directories));

        var error = Assert.Throws<ArgumentException>(() => subject.Write(useDefault ? default : []));

        Assert.Equal("image", error.ParamName);
        Assert.Equal(0, directories.Calls);
    }

    [Fact]
    public void AllocationFailureIsPreservedWithoutRetry()
    {
        var cause = new IOException("allocation failure");
        var directories = new RecordingDirectories("unused", cause);
        var subject = Assert.IsAssignableFrom<ICorpusAssemblyWriter>(new CorpusAssemblyWriter(directories));

        var actual = Assert.Throws<IOException>(() => subject.Write([1]));

        Assert.Same(cause, actual);
        Assert.Equal(1, directories.Calls);
    }

    private sealed class RecordingDirectories(string directory, Exception? failure = null) : ICorpusRunDirectoryFactory
    {
        public int Calls { get; private set; }

        public string Create()
        {
            Calls++;
            if (failure is not null)
            {
                throw failure;
            }
            return directory;
        }
    }
}
