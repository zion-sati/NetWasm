using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class NativeRuntimeMaterializationIntegrationTests
{
    private static readonly JsonSerializerOptions ReceiptOptions = new() { WriteIndented = true };

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeMaterializationIntegration")]
    public void MaterializesFromCompilerLayoutAndCurrentArchiveBytesWithColdWarmAndCorruptCache(string target, bool lto)
    {
        using var temporary = new TemporaryDirectory();
        var root = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT") ?? temporary.Path;
        var cell = Path.Combine(root, target + (lto ? "-lto" : "-object"));
        Assert.False(Directory.Exists(cell));
        Directory.CreateDirectory(cell);
        var repository = RepositoryRoot();
        var assetRoot = Path.Combine(repository, "src", "NetWasm.Runtime.Pack", "runtime");
        var manifest = Path.Combine(assetRoot, "runtime-pack.json");
        var emsdk = Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "emsdk");
        var producerTools = Path.Combine(emsdk, "upstream", "bin");
        var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
        var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var clang = Path.Combine(producerTools, "clang" + suffix);
        var archiver = Path.Combine(producerTools, "llvm-ar" + suffix);
        var commands = new CommandInvoker();
        var source = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets", "native-mule.c");
        var inputCopies = new[]
        {
            source, manifest, Path.Combine(assetRoot, "runtime-policy.json"),
            typeof(RuntimeMaterializationComposition).Assembly.Location,
            typeof(NativeRuntimeMaterializationIntegrationTests).Assembly.Location,
        };
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in inputCopies)
        {
            var copy = Path.Combine(cell, Path.GetFileName(input));
            File.Copy(input, copy);
            inputs.Add(Path.GetFileName(input), new Sha256ArtifactDigestCalculator().Calculate(copy));
        }
        var objectPath = Path.Combine(cell, "mule.o");
        var archive = Path.Combine(cell, "libmule.a");
        var compile = ImmutableArray.Create("--target=" + target + "-unknown-emscripten", "-O2", "-ffreestanding",
            "-ffunction-sections", "-fdata-sections", "-c", source, "-o", objectPath);
        if (lto) compile = compile.Add("-flto");
        commands.Invoke(new(clang, compile, Path.Combine(cell, "produce.log")));
        commands.Invoke(new(archiver, ["rcs", archive, objectPath], Path.Combine(cell, "archive.log")));
        var layout = Path.Combine(cell, "runtime-layout.json");
        File.WriteAllText(layout, JsonSerializer.Serialize(new
        {
            schemaVersion = 3, target, applicationStaticDataEnd = 65_537,
            nativeImports = new[]
            {
                new { libraryName = "mule", entryPoint = "native_mule_probe", parameters = Array.Empty<string>(), returnType = "i32" },
                new { libraryName = "mule", entryPoint = "native_mule_data_end", parameters = Array.Empty<string>(), returnType = target == "wasm64" ? "i64" : "i32" },
            },
        }));
        var request = new RuntimeMaterializationRequest(manifest, layout, assetRoot,
            Path.Combine(hostTools, "tools", "bin", "wasm-ld" + suffix),
            Path.Combine(hostTools, "tools", "bin", "wasm-opt" + suffix),
            Path.Combine(hostTools, "tools", "bin", "node" + suffix),
            Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
            Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm"),
            Path.Combine(cell, "runtime.wasm"), Path.Combine(cell, "logs"), Path.Combine(cell, "cache"),
            target, RuntimeWasmOptimization.Oz, 65_537, null,
            new("source-test", "source-test", "source-test", "source-test", "qualified-host-tools", "0.5.0",
                "emscripten-6.0.7", "emscripten-6.0.7", "qualified-wasm-tools", "qualified-node"))
        {
            NativeLibraries = [new("mule", target, archive), new("unused", target, Path.Combine(cell, "missing-unused.a")),
                new("mule", target == "wasm32" ? "wasm64" : "wasm32", Path.Combine(cell, "missing-other-target.a"))],
        };
        var materializer = RuntimeMaterializationComposition.Create();
        var cold = materializer.Materialize(request);
        Assert.True(cold.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, cold.CacheMetrics.Outcome);
        var bytes = File.ReadAllBytes(cold.OutputPath);
        var actual = new LinkedRuntimeModuleReader().Read(bytes);
        Assert.Equal(cold.HeapBase, actual.MemoryLayout.HeapBase);
        Assert.True(actual.MemoryLayout.DataEnd > cold.RuntimeGlobalBase + 2 * 1024 * 1024);
        Assert.True(bytes.Length < 3 * 1024 * 1024);
        Assert.DoesNotContain(actual.Exports, export => export.Name == "native_mule_unused");
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(cold.OutputPath, timestamp);
        var warm = materializer.Materialize(request);
        Assert.False(warm.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, warm.CacheMetrics.Outcome);
        Assert.Equal(cold.Sha256, warm.Sha256);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(cold.OutputPath));
        var archiveTime = File.GetLastWriteTimeUtc(archive);
        var extraSource = Path.Combine(cell, "extra.c");
        var extraObject = Path.Combine(cell, "extra.o");
        // Ordinary archive extraction selects the original provider member. The
        // alternative definition is valid while unextracted and must not make
        // whole-member format/target validation reject the archive.
        File.WriteAllText(extraSource, "int native_extra_unused(void) { return 12345; }\nint native_mule_probe(void) { return 12345; }\n");
        var extraCompile = ImmutableArray.Create("--target=" + target + "-unknown-emscripten", "-O2", "-ffreestanding",
            "-ffunction-sections", "-fdata-sections", "-c", extraSource, "-o", extraObject);
        if (lto) extraCompile = extraCompile.Add("-flto");
        commands.Invoke(new(clang, extraCompile, Path.Combine(cell, "produce-extra.log")));
        commands.Invoke(new(archiver, ["rcs", archive, extraObject], Path.Combine(cell, "archive-extra.log")));
        File.SetLastWriteTimeUtc(archive, archiveTime);
        var replaced = materializer.Materialize(request);
        Assert.True(replaced.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, replaced.CacheMetrics.Outcome);
        Assert.NotEqual(cold.CacheMetrics.KeyPrefix, replaced.CacheMetrics.KeyPrefix);
        Assert.Equal(bytes, File.ReadAllBytes(replaced.OutputPath));
        var slot = new RuntimeMaterializationCacheSlot(target, request.Optimization);
        var cachePath = RuntimeMaterializationCachePaths.Entry(request.CacheDirectory, slot);
        var envelope = File.ReadAllBytes(cachePath);
        envelope[^1] ^= 1;
        File.WriteAllBytes(cachePath, envelope);
        var repaired = materializer.Materialize(request);
        Assert.True(repaired.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, repaired.CacheMetrics.Outcome);
        Assert.Equal(replaced.Sha256, repaired.Sha256);
        var runs = Directory.GetDirectories(request.LogDirectory, "native-run-*");
        Assert.Equal(4, runs.Length);
        Assert.Single(runs, run => File.Exists(Path.Combine(run, "native-cache-validate.log")));
        Assert.All(runs.Where(run => !File.Exists(Path.Combine(run, "native-cache-validate.log"))), run =>
        {
            Assert.False(Directory.Exists(Path.Combine(run, "work")));
            Assert.True(File.Exists(Path.Combine(run, "native-probe-link.log")));
            Assert.True(File.Exists(Path.Combine(run, "native-final-link.log")));
        });
        File.WriteAllText(Path.Combine(cell, "receipt.json"), JsonSerializer.Serialize(new
        {
            Target = target, ArchiveKind = lto ? "lto" : "object", Cold = cold, Warm = warm,
            ReplacedArchive = replaced, RepairedCorruptCache = repaired, Observed = actual.MemoryLayout,
            ArchiveSha256 = new RuntimeNativeArchiveReader().Read(archive),
            OutputSha256 = new Sha256ArtifactDigestCalculator().Calculate(request.OutputPath),
            Inputs = inputs,
        }, ReceiptOptions));
    }

    private static string RequiredDirectory(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Directory.Exists(path));
        return path!;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "NetWasm.Runtime.Pack")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The integration test repository root is unavailable.");
    }
}
