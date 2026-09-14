using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class JcoClosureIntegrityVerifierTests
{
    [Fact]
    public void VerifyAcceptsEveryListedClosureFileThroughItsInterface()
    {
        using var directory = new TemporaryDirectory();
        var closureRoot = directory.Combine("tools", "jco");
        var assetPath = directory.Write(
            "tools/jco/node_modules/@bytecodealliance/jco/package.json",
            "package");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
        directory.Write(
            "tools/jco/closure-integrity.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[]
                {
                    new
                    {
                        path = "node_modules/@bytecodealliance/jco/package.json",
                        sha256 = digest,
                    },
                },
            }));

        IJcoClosureIntegrityVerifier verifier = CreateVerifier();
        verifier.Verify(directory.Path + Path.DirectorySeparatorChar, "tools/jco/closure-integrity.json");

        Assert.True(Directory.Exists(closureRoot));
    }

    [Fact]
    public void VerifyRejectsInvalidRootAndManifestPath()
    {
        var verifier = CreateVerifier();

        Assert.Throws<ArgumentException>(() => verifier.Verify("relative", "manifest.json"));
        Assert.Throws<ArgumentException>(() => verifier.Verify(Path.GetTempPath(), ""));
        Assert.Throws<ArgumentException>(() => verifier.Verify(Path.GetTempPath(), " "));
    }

    [Fact]
    public void VerifyRejectsMissingManifestAndAbsoluteManifestPath()
    {
        using var directory = new TemporaryDirectory();
        var verifier = CreateVerifier();

        Assert.Throws<ToolchainAssetMissingException>(() => verifier.Verify(directory.Path, "missing.json"));
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, Path.Combine(directory.Path, "missing.json")));
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "."));
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "../outside.json"));
    }

    [Fact]
    public void VerifyRejectsEmptyInvalidAndUnsupportedManifests()
    {
        using var directory = new TemporaryDirectory();
        var verifier = CreateVerifier();

        directory.Write("empty.json", "null");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "empty.json"));

        directory.Write("invalid.json", "{");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "invalid.json"));

        directory.Write("schema.json", "{\"schemaVersion\":\"2\",\"files\":[]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "schema.json"));

        directory.Write("files.json", "{\"schemaVersion\":\"1\",\"files\":[]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "files.json"));

    }

    [Fact]
    public void VerifyRejectsMalformedEntries()
    {
        using var directory = new TemporaryDirectory();
        var verifier = CreateVerifier();

        directory.Write("blank-path.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\" \",\"sha256\":\"abc\"}]}");
        Assert.Throws<ArgumentException>(() => verifier.Verify(directory.Path, "blank-path.json"));

        directory.Write("blank-digest.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"asset\",\"sha256\":\"\"}]}");
        Assert.Throws<ArgumentException>(() => verifier.Verify(directory.Path, "blank-digest.json"));

        directory.Write("asset", "actual");
        directory.Write("short-digest.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"asset\",\"sha256\":\"abc\"}]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "short-digest.json"));

        directory.Write("absolute-entry.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"/outside\",\"sha256\":\"abc\"}]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "absolute-entry.json"));

        directory.Write("unsafe-entry.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"../outside\",\"sha256\":\"abc\"}]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "unsafe-entry.json"));

        directory.Write("case-sibling.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"../JCO-OUTSIDE/asset\",\"sha256\":\"abc\"}]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "case-sibling.json"));

        directory.Write(
            "duplicate-entry.json",
            "{\"schemaVersion\":\"1\",\"files\":[{" +
            "\"path\":\"asset\",\"sha256\":\"" + new string('a', 64) +
            "},{\"path\":\"asset\",\"sha256\":\"" + new string('a', 64) + "\"}]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "duplicate-entry.json"));

        var duplicateAssetPath = directory.Write("node_modules/duplicate", "duplicate");
        var duplicateDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(duplicateAssetPath)));
        directory.Write(
            "duplicate-valid-entry.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[]
                {
                    new { path = "node_modules/duplicate", sha256 = duplicateDigest },
                    new { path = "node_modules/duplicate", sha256 = duplicateDigest },
                },
            }));
        Assert.Throws<InvalidDataException>(() => verifier.Verify(
            directory.Path,
            "duplicate-valid-entry.json"));

        var backslashPath = directory.Write("node_modules/backslash", "actual");
        var backslashDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backslashPath)));
        directory.Write(
            "backslash-entry.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[] { new { path = "node_modules\\backslash", sha256 = backslashDigest } },
            }));
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "backslash-entry.json"));
    }

    [Fact]
    public void VerifyRejectsAnUnlistedClosureFile()
    {
        using var directory = new TemporaryDirectory();
        var verifier = CreateVerifier();
        var assetPath = directory.Write("tools/jco/node_modules/asset", "actual");
        var listedPath = directory.Write("tools/jco/node_modules/other", "listed");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(listedPath)));
        directory.Write(
            "tools/jco/closure-integrity.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[] { new { path = "node_modules/other", sha256 = digest } },
            }));

        Assert.Throws<InvalidDataException>(() => verifier.Verify(
            directory.Path,
            "tools/jco/closure-integrity.json"));
        Assert.True(File.Exists(assetPath));
    }

    [Fact]
    public void VerifyRejectsMissingAndMismatchedClosureFiles()
    {
        using var directory = new TemporaryDirectory();
        var verifier = CreateVerifier();

        directory.Write("missing-file.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"missing\",\"sha256\":\"abc\"}]}");
        Assert.Throws<ToolchainAssetMissingException>(() => verifier.Verify(directory.Path, "missing-file.json"));

        var assetPath = directory.Write("asset", "actual");
        directory.Write("mismatch.json", "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"asset\",\"sha256\":\"abc\"}]}");
        Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, "mismatch.json"));
        Assert.True(File.Exists(assetPath));
    }

    [Fact]
    public void VerifyRejectsReparsePointFilesAndDirectories()
    {
        using (var directory = new TemporaryDirectory())
        using (var outside = new TemporaryDirectory())
        {
            var outsidePath = outside.Write("asset", "outside");
            var linkPath = directory.Combine("tools", "jco", "node_modules", "linked");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(linkPath)!);
            File.CreateSymbolicLink(linkPath, outsidePath);
            directory.Write(
                "tools/jco/closure-integrity.json",
                "{\"schemaVersion\":\"1\",\"files\":[{\"path\":\"node_modules/linked\",\"sha256\":\"" +
                new string('a', 64) + "\"}]}");

            var verifier = CreateVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(
                directory.Path,
                "tools/jco/closure-integrity.json"));
        }

        using (var directory = new TemporaryDirectory())
        using (var outside = new TemporaryDirectory())
        {
            var assetPath = directory.Write("tools/jco/node_modules/asset", "actual");
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
            var linkedDirectory = directory.Combine("tools", "jco", "node_modules", "linked");
            Directory.CreateSymbolicLink(linkedDirectory, outside.Path);
            directory.Write(
                "tools/jco/closure-integrity.json",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "1",
                    files = new[] { new { path = "node_modules/asset", sha256 = digest } },
                }));

            var verifier = CreateVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(
                directory.Path,
                "tools/jco/closure-integrity.json"));
        }

        using (var directory = new TemporaryDirectory())
        using (var outside = new TemporaryDirectory())
        {
            var assetPath = directory.Write("tools/jco-real/node_modules/asset", "actual");
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
            File.CreateSymbolicLink(
                directory.Combine("tools", "jco-real", "unlisted"),
                outside.Write("asset", "outside"));
            directory.Write(
                "tools/jco-real/closure-integrity.json",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "1",
                    files = new[] { new { path = "node_modules/asset", sha256 = digest } },
                }));
            Directory.CreateSymbolicLink(
                directory.Combine("tools", "jco"),
                directory.Combine("tools", "jco-real"));

            var verifier = CreateVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(
                directory.Path,
                "tools/jco/closure-integrity.json"));
        }

        using (var directory = new TemporaryDirectory())
        using (var outside = new TemporaryDirectory())
        {
            var assetPath = directory.Write("tools/jco/node_modules/asset", "actual");
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
            File.CreateSymbolicLink(
                directory.Combine("tools", "jco", "node_modules", "unlisted"),
                outside.Write("asset", "outside"));
            directory.Write(
                "tools/jco/closure-integrity.json",
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "1",
                    files = new[] { new { path = "node_modules/asset", sha256 = digest } },
                }));

            var verifier = CreateVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(
                directory.Path,
                "tools/jco/closure-integrity.json"));
        }
    }

    [Fact]
    public void VerifyAllowsOnlyTheDeclaredIntegrityMetadataFilesAsSidecars()
    {
        using var directory = new TemporaryDirectory();
        var assetPath = directory.Write("tools/jco/node_modules/asset", "actual");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
        directory.Write("tools/jco/notices.json", "third-party notices");
        directory.Write("tools/jco/closure-pack-items.props", "<Project />");
        directory.Write(
            "tools/jco/closure-integrity.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[] { new { path = "node_modules/asset", sha256 = digest } },
            }));

        CreateVerifier().Verify(directory.Path, "tools/jco/closure-integrity.json");
    }

    [Fact]
    public void JcoRejectsTheBundlerCommandButBundlerAllowsIt()
    {
        using var directory = new TemporaryDirectory();
        var assetPath = directory.Write("tools/closure/node_modules/asset", "actual");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
        directory.Write("tools/closure/netwasm-bundle.mjs", "export {};");
        directory.Write(
            "tools/closure/closure-integrity.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[] { new { path = "node_modules/asset", sha256 = digest } },
            }));

        Assert.Throws<InvalidDataException>(() => CreateVerifier().Verify(
            directory.Path,
            "tools/closure/closure-integrity.json"));

        var bundler = new HostingBundleClosureIntegrityVerifier();
        bundler.Verify(directory.Path, "tools/closure/closure-integrity.json");
    }

    [Fact]
    public void VerifyRejectsNullEntriesAndNonPortablePaths()
    {
        using var directory = new TemporaryDirectory();
        var verifier = CreateVerifier();

        directory.Write(
            "null-entry.json",
            "{\"schemaVersion\":\"1\",\"files\":[null]}");
        Assert.Throws<ArgumentNullException>(() => verifier.Verify(
            directory.Path,
            "null-entry.json"));

        foreach (var (name, path) in new[]
        {
            ("double-separator.json", "node_modules//asset"),
            ("reserved.json", "node_modules/CON.txt"),
            ("reserved-prn.json", "node_modules/PRN.txt"),
            ("reserved-aux.json", "node_modules/AUX.txt"),
            ("reserved-nul.json", "node_modules/NUL.txt"),
            ("reserved-com.json", "node_modules/COM1.txt"),
            ("reserved-lpt.json", "node_modules/LPT9.txt"),
            ("control.json", "node_modules/\u0001asset"),
            ("delete-control.json", "node_modules/\u007fasset"),
            ("ads.json", "node_modules/asset:stream"),
            ("less-than.json", "node_modules/asset<name"),
            ("greater-than.json", "node_modules/asset>name"),
            ("quote.json", "node_modules/asset\"name"),
            ("pipe.json", "node_modules/asset|name"),
            ("question.json", "node_modules/asset?name"),
            ("star.json", "node_modules/asset*name"),
            ("semicolon.json", "node_modules/asset;name"),
            ("property-expression.json", "node_modules/asset$(name"),
            ("item-expression.json", "node_modules/asset@(name"),
            ("metadata-expression.json", "node_modules/asset%(name"),
            ("trailing-dot.json", "node_modules/asset."),
        })
        {
            directory.Write(
                name,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "1",
                    files = new[] { new { path, sha256 = new string('a', 64) } },
                }));
            Assert.Throws<InvalidDataException>(() => verifier.Verify(directory.Path, name));
        }

        using var accepted = new TemporaryDirectory();
        var nonreservedPath = accepted.Write("tools/jco/node_modules/COMX.txt", "ordinary");
        var nonreservedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nonreservedPath)));
        accepted.Write(
            "tools/jco/closure-integrity.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[] { new { path = "node_modules/COMX.txt", sha256 = nonreservedDigest } },
            }));
        verifier.Verify(accepted.Path, "tools/jco/closure-integrity.json");
    }

    [Fact]
    public void VerifyRejectsCaseFoldedManifestPathAliases()
    {
        using var directory = new TemporaryDirectory();
        var assetPath = directory.Write("tools/jco/node_modules/asset", "actual");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath)));
        directory.Write(
            "tools/jco/closure-integrity.json",
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = new[]
                {
                    new { path = "node_modules/asset", sha256 = digest },
                    new { path = "node_modules/ASSET", sha256 = digest },
                },
            }));

        var exception = Assert.Throws<InvalidDataException>(() => CreateVerifier().Verify(
            directory.Path,
            "tools/jco/closure-integrity.json"));
        Assert.Contains("case or Unicode alias", exception.Message, StringComparison.Ordinal);
    }

    private static IJcoClosureIntegrityVerifier CreateVerifier()
    {
        IJcoClosureIntegrityVerifier verifier = new JcoClosureIntegrityVerifier();
        return verifier;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"netwasm-jco-integrity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] segments) => System.IO.Path.Combine([Path, .. segments]);

        public string Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
