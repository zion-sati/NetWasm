using System.Collections.Immutable;
using System.Security.Cryptography;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record PersistedCorpusAssembly(CorpusArtifact Artifact, string Directory);

internal interface ICorpusAssemblyWriter
{
    PersistedCorpusAssembly Write(ImmutableArray<byte> image);
}

internal sealed class CorpusAssemblyWriter(ICorpusRunDirectoryFactory directories) : ICorpusAssemblyWriter
{
    public PersistedCorpusAssembly Write(ImmutableArray<byte> image)
    {
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An emitted assembly image must not be empty.", nameof(image));
        }
        var directory = directories.Create();
        var path = Path.Combine(directory, "fixture.dll");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(image.AsSpan());
        }
        var artifact = new CorpusArtifact(
            path,
            string.Empty,
            Convert.ToHexStringLower(SHA256.HashData(image.AsSpan())),
            string.Empty,
            "not-applicable",
            ["input-kind:emitted", $"producer-runtime:{Environment.Version}"]);
        return new(artifact, directory);
    }
}
