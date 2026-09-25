using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusSourceFingerprintTests
{
    [Fact]
    public void PreservesTheLegacySingleSourceHash()
    {
        var fingerprint = Assert.IsAssignableFrom<ICorpusSourceFingerprint>(new CorpusSourceFingerprint());
        var fixture = CorrectnessTestAssets.CreateFixture() with { Source = "exact source\r\n" };

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Source))), fingerprint.Compute(fixture));
    }

    [Fact]
    public void BindsSecondaryNamesContentOrderAndFileBoundaries()
    {
        var fingerprint = Assert.IsAssignableFrom<ICorpusSourceFingerprint>(new CorpusSourceFingerprint());
        var fixture = CorrectnessTestAssets.CreateFixture() with
        {
            AdditionalSources = [new("First.cs", "first"), new("Second.cs", "second")],
        };
        var variants = new[]
        {
            fixture,
            fixture with { Source = "changed primary" },
            fixture with { AdditionalSources = [new("Renamed.cs", "first"), new("Second.cs", "second")] },
            fixture with { AdditionalSources = [new("First.cs", "changed"), new("Second.cs", "second")] },
            fixture with { AdditionalSources = [new("Second.cs", "second"), new("First.cs", "first")] },
            fixture with { AdditionalSources = [new("First.cs", "firstsecond")] },
            fixture with { AdditionalSources = [] },
        };

        var hashes = variants.Select(fingerprint.Compute).ToArray();

        Assert.Equal(variants.Length, hashes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(hashes, hash => Assert.Equal(64, hash.Length));
        Assert.Equal(hashes[0], fingerprint.Compute(fixture with { AdditionalSources = [.. fixture.AdditionalSources] }));
    }

    [Fact]
    public void RejectsMissingSourceDeclarations()
    {
        var fingerprint = Assert.IsAssignableFrom<ICorpusSourceFingerprint>(new CorpusSourceFingerprint());

        Assert.Throws<ArgumentNullException>(() => fingerprint.Compute(null!));
        Assert.Throws<ArgumentNullException>(() => fingerprint.Compute(CorrectnessTestAssets.CreateFixture() with { Source = null! }));
        Assert.Throws<ArgumentException>(() => fingerprint.Compute(CorrectnessTestAssets.CreateFixture() with
        {
            AdditionalSources = default(ImmutableArray<CorpusSourceFile>),
        }));
    }
}
