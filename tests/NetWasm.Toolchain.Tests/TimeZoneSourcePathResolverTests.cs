// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class TimeZoneSourcePathResolverTests
{
    [Fact]
    public void ResolveVerifiesTheIntegrityChainAndReturnsThePinnedSource()
    {
        using var package = TimeZonePackage.Create("2026c");
        var resolver = Assert.IsAssignableFrom<ITimeZoneSourcePathResolver>(
            new TimeZoneSourcePathResolver(package.Root, package.Assets()));

        var result = resolver.Resolve();

        Assert.Equal("NetWasm.Toolchain", result.PackageId);
        Assert.Equal("0.1.0-preview.29", result.PackageVersion);
        Assert.Equal(Path.Combine(package.Root, "tools", "timezones", "zoneinfo"), result.SourceRoot);
        Assert.Equal("2026c", result.DataVersion);
    }

    [Fact]
    public void ResolveRejectsTamperedExtraMissingAndMismatchedSources()
    {
        using (var tampered = TimeZonePackage.Create("2026c"))
        {
            File.WriteAllText(tampered.ZonePath, "TZif changed");
            Assert.Throws<InvalidDataException>(() =>
                new TimeZoneSourcePathResolver(tampered.Root, tampered.Assets()).Resolve());
        }
        using (var extra = TimeZonePackage.Create("2026c"))
        {
            File.WriteAllText(Path.Combine(extra.SourceRoot, "Unlisted"), "TZif");
            Assert.Throws<InvalidDataException>(() =>
                new TimeZoneSourcePathResolver(extra.Root, extra.Assets()).Resolve());
        }
        using (var mismatched = TimeZonePackage.Create("2026b"))
        {
            var error = Assert.Throws<InvalidDataException>(() =>
                new TimeZoneSourcePathResolver(mismatched.Root, mismatched.Assets("2026c")).Resolve());
            Assert.Contains("IANA 2026b, but the package pins 2026c", error.Message, StringComparison.Ordinal);
        }
        using (var unversioned = TimeZonePackage.Create("2026c", includeVersion: false))
        {
            Assert.Throws<ToolchainAssetMissingException>(() =>
                new TimeZoneSourcePathResolver(unversioned.Root, unversioned.Assets()).Resolve());
        }
        using (var unsigned = TimeZonePackage.Create("2026c"))
        {
            Assert.Throws<InvalidDataException>(() => new TimeZoneSourcePathResolver(
                unsigned.Root, unsigned.Assets(digest: new string('a', 64))).Resolve());
        }
    }

    [Fact]
    public void ConstructorRequiresAnAbsoluteRootAndAssetResolver()
    {
        using var package = TimeZonePackage.Create("2026c");
        Assert.Throws<ArgumentException>(() => new TimeZoneSourcePathResolver("relative", package.Assets()));
        Assert.Throws<ArgumentException>(() => new TimeZoneSourcePathResolver(" ", package.Assets()));
        Assert.Throws<ArgumentNullException>(() => new TimeZoneSourcePathResolver(package.Root, null!));
    }

    [Fact]
    public void VendoredSourceMatchesTheIanaPinAndItsManifestEntry()
    {
        var repository = FindRepositoryRoot();
        var toolchain = Path.Combine(repository, "src", "NetWasm.Toolchain");
        using var pins = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "eng", "toolchain.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(toolchain, "toolchain-manifest.json")));
        var asset = manifest.RootElement.GetProperty("assets").EnumerateArray()
            .Single(entry => entry.GetProperty("id").GetString() == ToolchainPlatformAssetIds.TimeZoneClosureIntegrity);
        using var package = new TemporaryDirectory();
        CopyDirectory(Path.Combine(toolchain, "timezones"), Path.Combine(package.Path, "tools", "timezones"));
        var descriptor = new PinnedPlatformAssetDescriptor
        {
            Id = ToolchainPlatformAssetIds.TimeZoneClosureIntegrity,
            Version = asset.GetProperty("version").GetString()!,
            RelativePath = asset.GetProperty("relativePath").GetString()!,
            Sha256 = asset.GetProperty("sha256").GetString()!,
        };

        var result = new TimeZoneSourcePathResolver(package.Path, AssetResolver(package.Path, descriptor)).Resolve();

        Assert.Equal(pins.RootElement.GetProperty("ianaTimeZones").GetString(), result.DataVersion);
        Assert.Equal(result.DataVersion, descriptor.Version);
        Assert.True(File.Exists(Path.Combine(result.SourceRoot, "tzdata.zi")));
        Assert.True(File.Exists(Path.Combine(result.SourceRoot, "Australia", "Melbourne")));
        Assert.False(File.Exists(Path.Combine(result.SourceRoot, "Asia", "Calcutta")));
    }

    private static PinnedPlatformAssetPathResolver AssetResolver(string root, PinnedPlatformAssetDescriptor descriptor) =>
        new(root, new PinnedToolchainManifest
        {
            SchemaVersion = "1",
            PackageId = "NetWasm.Toolchain",
            PackageVersion = "0.1.0-preview.29",
            Assets = [descriptor],
        }, new FilePresenceChecker(), new Sha256ArtifactDigestVerifier());

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "eng", "toolchain.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed class TimeZonePackage : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        private TimeZonePackage()
        {
        }

        public string Root => _directory.Path;

        public string SourceRoot => Path.Combine(Root, "tools", "timezones", "zoneinfo");

        public string ZonePath => Path.Combine(SourceRoot, "Area", "Zone");

        private string IntegrityPath => Path.Combine(Root, "tools", "timezones", "closure-integrity.json");

        public static TimeZonePackage Create(string version, bool includeVersion = true)
        {
            var package = new TimeZonePackage();
            Directory.CreateDirectory(Path.GetDirectoryName(package.ZonePath)!);
            File.WriteAllText(package.ZonePath, "TZif zone");
            var files = new List<(string Path, string Sha256)> { ("zoneinfo/Area/Zone", Digest(package.ZonePath)) };
            if (includeVersion)
            {
                var versionPath = Path.Combine(package.SourceRoot, "version");
                File.WriteAllText(versionPath, $"{version}\n");
                files.Add(("zoneinfo/version", Digest(versionPath)));
            }
            File.WriteAllText(package.IntegrityPath, JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                files = files.Select(file => new { path = file.Path, sha256 = file.Sha256 }),
            }));
            return package;
        }

        public PinnedPlatformAssetPathResolver Assets(string version = "2026c", string? digest = null) =>
            AssetResolver(Root, new PinnedPlatformAssetDescriptor
            {
                Id = ToolchainPlatformAssetIds.TimeZoneClosureIntegrity,
                Version = version,
                RelativePath = "tools/timezones/closure-integrity.json",
                Sha256 = digest ?? Digest(IntegrityPath),
            });

        public void Dispose() => _directory.Dispose();

        private static string Digest(string path) =>
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"netwasm-timezones-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
