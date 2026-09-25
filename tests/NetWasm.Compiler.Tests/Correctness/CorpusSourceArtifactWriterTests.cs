using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusSourceArtifactWriterTests
{
    [Fact]
    public void MaterializesTheFixtureSourceInEachShardDirectory()
    {
        var fixture = CorrectnessTestAssets.CreateFixture("CachedTemplateShard");
        var directory = Directory.CreateTempSubdirectory("netwasm-source-writer-");
        var writer = Assert.IsAssignableFrom<ICorpusSourceArtifactWriter>(new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        try
        {
            var artifact = writer.Write(new(fixture.Name + ".cs", fixture.Source), directory.FullName);

            Assert.Equal(Path.Combine(directory.FullName, fixture.Name + ".cs"), artifact.Path);
            Assert.Equal(fixture.Name + ".cs", artifact.Name);
            Assert.Equal(fixture.Source, File.ReadAllText(artifact.Path));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Source))), artifact.Sha256);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("nested/Part.cs", "namespace Example;\r\n")]
    [InlineData("nested/Part.cs.txt", "")]
    [InlineData("Unicode.cs", "// café ∞ 😀\n")]
    public void MaterializesNestedUnitsWithoutChangingContent(string name, string content)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-source-writer-");
        var writer = Assert.IsAssignableFrom<ICorpusSourceArtifactWriter>(new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        try
        {
            var artifact = writer.Write(new(name, content), Path.Combine(directory.FullName, "output"));

            Assert.Equal(name, artifact.Name);
            Assert.Equal(Encoding.UTF8.GetBytes(content), File.ReadAllBytes(artifact.Path));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), artifact.Sha256);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsInvalidRequestsBeforeCreatingArtifacts(int invalid)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-source-writer-");
        var writer = Assert.IsAssignableFrom<ICorpusSourceArtifactWriter>(new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        var source = invalid switch
        {
            0 => null,
            1 => new CorpusSourceFile("Source.cs", null!),
            2 => new CorpusSourceFile("../Outside.cs", "source"),
            _ => new CorpusSourceFile("Source.cs", "source"),
        };
        try
        {
            Assert.ThrowsAny<ArgumentException>(() => writer.Write(source!, invalid == 3 ? " " : Path.Combine(directory.FullName, "output")));
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete();
        }
    }

    [Fact]
    public void PreservesValidationFailureBeforeMaterialization()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-source-writer-");
        var cause = new ArgumentException("source validation");
        var names = new RejectingNames(cause);
        var writer = Assert.IsAssignableFrom<ICorpusSourceArtifactWriter>(new CorpusSourceArtifactWriter(names));
        try
        {
            Assert.Same(cause, Assert.Throws<ArgumentException>(() => writer.Write(new("Source.cs", "source"), directory.FullName)));
            Assert.Equal<string>(["Source.cs"], names.Names);
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete();
        }
    }

    [Theory]
    [InlineData("original")]
    [InlineData("replacement")]
    public void RefusesToOverwriteAnExistingSource(string replacement)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-source-writer-");
        var writer = Assert.IsAssignableFrom<ICorpusSourceArtifactWriter>(new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        try
        {
            var artifact = writer.Write(new("nested/Source.cs", "original"), directory.FullName);
            var originalBytes = File.ReadAllBytes(artifact.Path);

            Assert.Throws<IOException>(() => writer.Write(new("nested/Source.cs", replacement), directory.FullName));

            Assert.Equal(originalBytes, File.ReadAllBytes(artifact.Path));
            Assert.Equal(artifact.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(artifact.Path))));
            Assert.Single(directory.EnumerateFiles("*", SearchOption.AllDirectories));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RejectsInvalidUnicodeBeforeCreatingArtifacts()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-source-writer-");
        var writer = Assert.IsAssignableFrom<ICorpusSourceArtifactWriter>(new CorpusSourceArtifactWriter(new CorpusSourceNamesVerifier()));
        try
        {
            Assert.Throws<EncoderFallbackException>(() => writer.Write(new("Source.cs", "\uD800"), Path.Combine(directory.FullName, "output")));
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RejectingNames(Exception cause) : ICorpusSourceNamesVerifier
    {
        public ImmutableArray<string> Names { get; private set; }

        public void Verify(ImmutableArray<string> sourceNames)
        {
            Names = sourceNames;
            throw cause;
        }
    }
}
