using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RuntimeJitCorpusTests
{
    private static readonly string[] AllowedDispositions = ["run"];

    [Fact]
    public void WhitelistIsLicensedCompleteAndByteTraceable()
    {
        var (manifest, root) = RuntimeJitCorpus.Load();

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(40, manifest.Commit.Length);
        Assert.Equal("MIT", manifest.License.Spdx);
        Assert.Equal(12, manifest.Entries.Length);
        Assert.Equal(5, manifest.Entries.Select(entry => entry.Cohort).Distinct().Count());
        Assert.Equal(5, manifest.Entries.Where(entry => entry.Disposition == "run")
            .Select(entry => entry.Cohort).Distinct().Count());
        Assert.All(manifest.Entries, entry =>
        {
            Assert.Contains(entry.Disposition, AllowedDispositions);
            Assert.Null(entry.Rationale);
            Assert.Equal("same-il", entry.OracleMode);
            Assert.NotEmpty(entry.Adaptation);
            Assert.NotEmpty(entry.RequiredFeatures);
            Assert.Equal(entry.Sha256, Hash(Path.Combine(root, entry.VendoredPath)));
            Assert.DoesNotContain('\r', RuntimeJitCorpus.CreateFixture(root, entry).Source);
        });
        Assert.Equal(
            manifest.License.Sha256,
            Hash(Path.Combine(root, manifest.License.VendoredPath)));
    }

    [Fact]
    public void EveryWhitelistedCohortPassesSameIlDifferentialExecution()
    {
        var (manifest, root) = RuntimeJitCorpus.Load();
        using var services = CorrectnessTestAssets.CreateServices(root);
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();

        foreach (var entry in manifest.Entries.Where(entry => entry.Disposition == "run"))
        {
            runner.Run(RuntimeJitCorpus.CreateFixture(root, entry));
        }
    }

    [Fact]
    public void CompilerGeneratedStaticArrayInitializerMatchesDesktopDotNet()
    {
        var (manifest, root) = RuntimeJitCorpus.Load();
        var entry = Assert.Single(manifest.Entries,
            item => item.Id == "il-conformance-int-array-sum");
        Assert.Equal("run", entry.Disposition);
        using var services = CorrectnessTestAssets.CreateServices(root);

        services
            .GetRequiredService<IDifferentialCorpusRunner>()
            .Run(RuntimeJitCorpus.CreateFixture(root, entry));
    }

    private static string Hash(string path) => Convert.ToHexStringLower(
        SHA256.HashData(File.ReadAllBytes(path)));
}
