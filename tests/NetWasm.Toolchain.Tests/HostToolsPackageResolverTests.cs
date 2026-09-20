using System.Collections.Immutable;
using System.Security.Cryptography;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Tests;

public sealed class HostToolsPackageResolverTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "netwasm-host-tools-unit");
    private const string PackageId = "NetWasm.HostTools.osx-arm64";
    private const string Version = "0.2.5-preview.1";

    [Fact]
    public void ResolveReturnsOnlyValidatedRestoredExecutables()
    {
        var fixture = Fixture.Create();

        var result = fixture.Resolver.Resolve(Request());

        Assert.Equal(PackageId, result.PackageId);
        Assert.Equal("26.7.0", result.NodeVersion);
        Assert.Equal("24.0.0", result.WasmLdVersion);
        Assert.Equal("132", result.BinaryenVersion);
        Assert.Equal(4, result.Roles.Count);
        Assert.Equal(Path.Combine(Root, "tools", "bin", "node"), result.Roles["node"]);
        Assert.Equal(13, fixture.Files.ReadPaths.Count);
    }

    [Fact]
    public void LinuxPackageValidatesItsBundledNodeDependencyBeforeReturningPaths()
    {
        var request = Request("linux-x64");
        var valid = Fixture.Create(rid: "linux-x64");

        Assert.Equal(4, valid.Resolver.Resolve(request).Roles.Count);
        Assert.Equal(17, valid.Files.ReadPaths.Count);
        Assert.Throws<InvalidDataException>(() =>
            Fixture.Create("missing-libatomic", "linux-x64").Resolver.Resolve(request));
        Assert.Throws<InvalidDataException>(() =>
            Fixture.Create("changed-libatomic", "linux-x64").Resolver.Resolve(request));
    }

    [Theory]
    [InlineData("wrong-identity")]
    [InlineData("wrong-version")]
    [InlineData("wrong-host")]
    [InlineData("bad-tool-version")]
    [InlineData("missing-role")]
    [InlineData("missing-notice")]
    [InlineData("duplicate-file")]
    public void ResolveRejectsInvalidManifestBeforeReadingPayload(string defect)
    {
        var fixture = Fixture.Create(defect);

        Assert.Throws<InvalidDataException>(() => fixture.Resolver.Resolve(Request()));

        Assert.Equal([Path.Combine(Root, "tools", "host-tools-manifest.json")],
            fixture.Files.ReadPaths);
    }

    [Theory]
    [InlineData("changed-bytes")]
    [InlineData("symlink")]
    [InlineData("missing")]
    [InlineData("no-execute")]
    public void ResolveRejectsCorruptOrUnavailablePayload(string defect)
    {
        var fixture = Fixture.Create(defect);

        var error = Assert.Throws<InvalidDataException>(() => fixture.Resolver.Resolve(Request()));

        Assert.Contains("Host-tools package", error.Message, StringComparison.Ordinal);
        if (defect == "missing")
        {
            Assert.IsType<FileNotFoundException>(error.InnerException);
        }
    }

    [Fact]
    public void ResolveRejectsARelativeRootBeforeReadingThePackage()
    {
        var fixture = Fixture.Create();

        Assert.Throws<ArgumentException>(() => fixture.Resolver.Resolve(
            Request() with { PackageRoot = "relative-root" }));

        Assert.Empty(fixture.Files.ReadPaths);
    }

    [Fact]
    public void JsonReaderRejectsUnsupportedSchemaAndMissingRequiredFields()
    {
        var reader = new JsonHostToolsPackageManifestReader();

        Assert.Throws<InvalidDataException>(() => reader.Read("{\"schemaVersion\":2}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => reader.Read("{\"schemaVersion\":1}"u8.ToArray()));
    }

    private static HostToolsPackageRequest Request(string rid = "osx-arm64") =>
        new(Root, $"NetWasm.HostTools.{rid}", Version, rid);

    private sealed class Fixture
    {
        private Fixture(HostToolsPackageResolver resolver, RecordingFileReader files)
        {
            Resolver = resolver;
            Files = files;
        }

        internal HostToolsPackageResolver Resolver { get; }
        internal RecordingFileReader Files { get; }

        internal static Fixture Create(string? defect = null, string rid = "osx-arm64")
        {
            var roles = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
                new Dictionary<string, string>
                {
                    ["node"] = "tools/bin/node",
                    ["wasm-ld"] = "tools/bin/wasm-ld",
                    ["wasm-opt"] = "tools/bin/wasm-opt",
                    ["wasm-merge"] = "tools/bin/wasm-merge",
                });
            if (defect == "missing-role")
            {
                roles = roles.Remove("wasm-merge");
            }
            var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["LICENSE.txt"] = "combined"u8.ToArray(),
                ["README.md"] = "readme"u8.ToArray(),
                ["licenses/Node-LICENSE"] = "node license"u8.ToArray(),
                ["licenses/LLVM-LICENSE.txt"] = "llvm license"u8.ToArray(),
                ["licenses/LLD-LICENSE.txt"] = "lld license"u8.ToArray(),
                ["licenses/LLVM-BLAKE3-LICENSE"] = "blake3 license"u8.ToArray(),
                ["licenses/LLVM-ThirdParty-NOTICES.txt"] = "embedded notices"u8.ToArray(),
                ["licenses/Binaryen-LICENSE"] = "binaryen license"u8.ToArray(),
                ["tools/bin/node"] = "node"u8.ToArray(),
                ["tools/bin/wasm-ld"] = "lld"u8.ToArray(),
                ["tools/bin/wasm-opt"] = "opt"u8.ToArray(),
                ["tools/bin/wasm-merge"] = "merge"u8.ToArray(),
            };
            if (rid.StartsWith("linux-", StringComparison.Ordinal))
            {
                payload["tools/bin/libatomic.so.1"] = "libatomic"u8.ToArray();
                payload["licenses/GCC-Libatomic-COPYRIGHT"] = "gcc copyright"u8.ToArray();
                payload["licenses/GPL-3.0.txt"] = "gpl3"u8.ToArray();
                payload["licenses/GCC-Libatomic-SOURCE.txt"] = "gcc source"u8.ToArray();
            }
            var files = payload.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new HostToolsPackageFile(
                    entry.Key, entry.Value.Length,
                    Convert.ToHexString(SHA256.HashData(entry.Value)).ToLowerInvariant()))
                .ToImmutableArray();
            if (defect == "missing-notice")
            {
                files = files.RemoveAt(2);
            }
            if (defect == "duplicate-file")
            {
                files = files.Add(files[0]);
            }
            var manifest = new HostToolsPackageManifest(
                defect == "wrong-identity" ? "NetWasm.HostTools.win-x64" : $"NetWasm.HostTools.{rid}",
                defect == "wrong-version" ? "0.1.0" : Version,
                defect == "wrong-host" ? "win-x64" : rid,
                defect == "bad-tool-version" ? "bad" : "26.7.0",
                "24.0.0", "132", roles, files);
            var content = payload.ToDictionary(
                entry => Path.Combine(Root, entry.Key.Replace('/', Path.DirectorySeparatorChar)),
                entry => new HostToolsPackageFileContents(entry.Value, false, true),
                StringComparer.Ordinal);
            content[Path.Combine(Root, "tools", "host-tools-manifest.json")] =
                new("{}"u8.ToArray(), false, false);
            var nodePath = Path.Combine(Root, "tools", "bin", "node");
            if (defect == "changed-bytes")
            {
                content[nodePath] = new("altered"u8.ToArray(), false, true);
            }
            if (defect == "symlink")
            {
                content[nodePath] = new("node"u8.ToArray(), true, true);
            }
            if (defect == "no-execute")
            {
                content[nodePath] = new("node"u8.ToArray(), false, false);
            }
            if (defect == "missing")
            {
                content.Remove(nodePath);
            }
            var libatomicPath = Path.Combine(Root, "tools", "bin", "libatomic.so.1");
            if (defect == "missing-libatomic")
            {
                content.Remove(libatomicPath);
            }
            if (defect == "changed-libatomic")
            {
                content[libatomicPath] = new("altered"u8.ToArray(), false, true);
            }
            var reader = new RecordingFileReader(content);
            return new(new(reader, new RecordingManifestReader(manifest)), reader);
        }
    }

    private sealed class RecordingFileReader(
        Dictionary<string, HostToolsPackageFileContents> contents) : IHostToolsPackageFileReader
    {
        internal List<string> ReadPaths { get; } = [];

        public HostToolsPackageFileContents Read(string absolutePath)
        {
            ReadPaths.Add(absolutePath);
            return contents.TryGetValue(absolutePath, out var content)
                ? content
                : throw new FileNotFoundException("Missing test payload.", absolutePath);
        }
    }

    private sealed class RecordingManifestReader(HostToolsPackageManifest manifest)
        : IHostToolsPackageManifestReader
    {
        public HostToolsPackageManifest Read(ReadOnlyMemory<byte> json) => manifest;
    }
}
