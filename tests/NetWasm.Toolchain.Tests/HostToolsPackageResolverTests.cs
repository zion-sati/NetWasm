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
        Assert.Equal(Path.Combine(Root, "tools", "bin", "nwc"), result.Roles["nwc"]);
        Assert.Equal(15, fixture.Files.ReadPaths.Count);
    }

    [Fact]
    public void ResolvesAnOlderPackageThatCarriesWasmMergeWithoutUsingIt()
    {
        var fixture = Fixture.Create("with-legacy-merge");

        var result = fixture.Resolver.Resolve(Request());

        // The copy is verified with the rest of the package; the compiler merges modules itself.
        Assert.Equal(4, result.Roles.Count);
        Assert.False(result.Roles.ContainsKey("wasm-merge"));
        Assert.Contains(Path.Combine(Root, "tools", "bin", "wasm-merge"), fixture.Files.ReadPaths);
        Assert.Equal(16, fixture.Files.ReadPaths.Count);
    }

    [Fact]
    public void ResolvesAnOlderPackageThatCarriesNoCompiler()
    {
        var fixture = Fixture.Create("without-compiler");

        var result = fixture.Resolver.Resolve(Request());

        Assert.Equal(3, result.Roles.Count);
        Assert.False(result.Roles.ContainsKey("nwc"));
        Assert.Equal(12, fixture.Files.ReadPaths.Count);
    }

    [Fact]
    public void ResolvesTheNativeWasmToolsOfAPackageThatCarriesIt()
    {
        var fixture = Fixture.Create("with-wasm-tools");

        var result = fixture.Resolver.Resolve(Request());

        Assert.Equal(5, result.Roles.Count);
        Assert.Equal(Path.Combine(Root, "tools", "bin", "wasm-tools"), result.Roles["wasm-tools"]);
        Assert.Equal("1.256.0", result.WasmToolsVersion);
        Assert.Equal(19, fixture.Files.ReadPaths.Count);
        Assert.Null(Fixture.Create().Resolver.Resolve(Request()).WasmToolsVersion);
    }

    [Fact]
    public void LinuxPackageValidatesItsBundledNodeDependencyBeforeReturningPaths()
    {
        var request = Request("linux-x64");
        var valid = Fixture.Create(rid: "linux-x64");

        Assert.Equal(4, valid.Resolver.Resolve(request).Roles.Count);
        Assert.Equal(19, valid.Files.ReadPaths.Count);
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
    [InlineData("legacy-merge-elsewhere")]
    [InlineData("compiler-without-notices")]
    [InlineData("missing-notice")]
    [InlineData("duplicate-file")]
    [InlineData("wasm-tools-without-notices")]
    [InlineData("wasm-tools-without-version")]
    [InlineData("wasm-tools-bad-version")]
    [InlineData("version-without-wasm-tools")]
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

    [Fact]
    public void JsonReaderReadsTheWasmToolsVersionOfAPackageThatCarriesIt()
    {
        var reader = new JsonHostToolsPackageManifestReader();
        static byte[] Manifest(string wasmTools) => System.Text.Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"packageId\":\"NetWasm.HostTools.osx-arm64\",\"packageVersion\":\"0.1.0\"," +
            "\"hostRid\":\"osx-arm64\",\"upstream\":{\"node\":\"26.7.0\",\"llvmLld\":{\"version\":\"24.0.0\"}," +
            "\"binaryen\":\"132\"" + wasmTools + "},\"roles\":{\"node\":\"tools/bin/node\"}," +
            "\"files\":[{\"path\":\"tools/bin/node\",\"size\":4,\"sha256\":\"00\"}]}");

        var carrying = reader.Read(Manifest(",\"wasmTools\":\"1.256.0\""));
        var older = reader.Read(Manifest(""));

        Assert.Equal(("26.7.0", "24.0.0", "132", "1.256.0"),
            (carrying.NodeVersion, carrying.WasmLdVersion, carrying.BinaryenVersion, carrying.WasmToolsVersion));
        Assert.Equal("tools/bin/node", Assert.Single(carrying.Roles).Value);
        Assert.Equal(4, Assert.Single(carrying.Files).Size);
        Assert.Null(older.WasmToolsVersion);
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
                    ["nwc"] = "tools/bin/nwc",
                });
            if (defect == "missing-role")
            {
                roles = roles.Remove("wasm-opt");
            }
            var carriesMerge = defect is "with-legacy-merge" or "legacy-merge-elsewhere";
            if (carriesMerge)
            {
                roles = roles.Add("wasm-merge",
                    defect == "with-legacy-merge" ? "tools/bin/wasm-merge" : "tools/wasm-merge");
            }
            if (defect == "without-compiler")
            {
                roles = roles.Remove("nwc");
            }
            var carriesWasmTools = defect is "with-wasm-tools" or "wasm-tools-without-notices"
                or "wasm-tools-without-version" or "wasm-tools-bad-version";
            if (carriesWasmTools)
            {
                roles = roles.Add("wasm-tools", "tools/bin/wasm-tools");
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
            };
            if (carriesMerge)
            {
                payload["tools/bin/wasm-merge"] = "merge"u8.ToArray();
            }
            if (defect != "without-compiler")
            {
                payload["tools/bin/nwc"] = "nwc"u8.ToArray();
                if (defect != "compiler-without-notices")
                {
                    payload["licenses/NetWasm-LICENSE.txt"] = "netwasm license"u8.ToArray();
                    payload["licenses/nwc-THIRD-PARTY-NOTICES.txt"] = "nwc notices"u8.ToArray();
                }
            }
            if (carriesWasmTools)
            {
                payload["tools/bin/wasm-tools"] = "wasm-tools"u8.ToArray();
                if (defect != "wasm-tools-without-notices")
                {
                    payload["licenses/wasm-tools-LICENSE-APACHE"] = "apache"u8.ToArray();
                    payload["licenses/wasm-tools-LICENSE-Apache-2.0_WITH_LLVM-exception"] = "apache llvm"u8.ToArray();
                    payload["licenses/wasm-tools-LICENSE-MIT"] = "mit"u8.ToArray();
                }
            }
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
                "24.0.0", "132", roles, files)
            {
                WasmToolsVersion = defect switch
                {
                    "with-wasm-tools" or "wasm-tools-without-notices" or "version-without-wasm-tools" => "1.256.0",
                    "wasm-tools-bad-version" => "bad",
                    _ => null,
                },
            };
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
