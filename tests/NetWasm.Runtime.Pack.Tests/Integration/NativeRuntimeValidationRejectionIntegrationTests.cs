using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Runtime.Pack.Composition;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Integration;

public sealed class NativeRuntimeValidationRejectionIntegrationTests
{
    private static readonly string[] Targets = ["wasm32", "wasm64"];
    private static readonly RuntimeWasmOptimization[] Optimizations = [RuntimeWasmOptimization.None, RuntimeWasmOptimization.Oz];
    private static readonly string[] ImportKinds = ["preview1", "arbitrary-host", "wrong-width", "wrong-signature"];
    private static readonly JsonSerializerOptions ReceiptOptions = new() { WriteIndented = true };

    public static TheoryData<string, RuntimeWasmOptimization, string> ImportCases
    {
        get
        {
            var result = new TheoryData<string, RuntimeWasmOptimization, string>();
            foreach (var target in Targets)
                foreach (var optimization in Optimizations)
                    foreach (var kind in ImportKinds)
                        result.Add(target, optimization, kind);
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(ImportCases))]
    [Trait("Category", "NativeValidationRejectionIntegration")]
    public void RejectsExplicitNativeHostImportsBeforeOptimizationOrPublication(string target, RuntimeWasmOptimization optimization, string kind)
    {
        using var fixture = new Fixture(target, optimization, kind);
        var otherPrefix = target == "wasm64" ? "cm32p2" : "cm64p2";
        var identity = kind switch
        {
            "preview1" => (Module: "wasi_snapshot_preview1", Name: "fd_write"),
            "arbitrary-host" => (Module: "native.custom.host", Name: "call"),
            "wrong-width" => (Module: otherPrefix + "|wasi:cli/exit@0.2", Name: "exit"),
            "wrong-signature" => (Module: (target == "wasm64" ? "cm64p2" : "cm32p2") + "|wasi:cli/stdout@0.2", Name: "get-stdout"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var source = $$"""
            __attribute__((import_module("{{identity.Module}}"), import_name("{{identity.Name}}")))
            extern int native_dependency(int);
            int native_probe(void) { return native_dependency(7); }
            """;
        fixture.Produce(source, target, false, false);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());

        Assert.Equal("The native-linked runtime imports do not match the supported runtime contract.", error.Message);
        fixture.AssertUnpublished();
        var run = Assert.Single(Directory.GetDirectories(fixture.Request.LogDirectory, "native-run-*"));
        Assert.True(File.Exists(Path.Combine(run, "native-probe-link.log")));
        Assert.False(File.Exists(Path.Combine(run, "native-final-link.log")));
        Assert.False(File.Exists(Path.Combine(run, "native-optimize.log")));
        fixture.WriteReceipt("import-contract");
    }

    [Theory]
    [InlineData("wasm32", RuntimeWasmOptimization.None)]
    [InlineData("wasm32", RuntimeWasmOptimization.Oz)]
    [InlineData("wasm64", RuntimeWasmOptimization.None)]
    [InlineData("wasm64", RuntimeWasmOptimization.Oz)]
    [Trait("Category", "NativeValidationRejectionIntegration")]
    public void RejectsUnsupportedInstructionsEvenWithoutTargetFeatureMetadata(string target, RuntimeWasmOptimization optimization)
    {
        using var fixture = new Fixture(target, optimization, "unsupported-feature");
        fixture.Produce("""
            typedef unsigned vector4 __attribute__((vector_size(16)));
            volatile vector4 native_values;
            int native_probe(void) {
                vector4 next = native_values + (vector4){1, 2, 3, 4};
                native_values = next;
                return (int)next[0];
            }
            """, target, false, true);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());

        Assert.Contains("A NetWasm runtime tool failed", error.Message, StringComparison.Ordinal);
        fixture.AssertUnpublished();
        var run = Assert.Single(Directory.GetDirectories(fixture.Request.LogDirectory, "native-run-*"));
        Assert.True(File.Exists(Path.Combine(run, "native-final-link.log")));
        Assert.True(File.Exists(Path.Combine(run, optimization == RuntimeWasmOptimization.None ? "native-validate.log" : "native-optimize.log")));
        fixture.WriteReceipt("feature-profile");
    }

    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm32", true)]
    [InlineData("wasm64", false)]
    [InlineData("wasm64", true)]
    [Trait("Category", "NativeValidationRejectionIntegration")]
    public void RejectsWrongWidthObjectAndLtoMembersDespiteDeclaredTargetMetadata(string target, bool lto)
    {
        using var fixture = new Fixture(target, RuntimeWasmOptimization.None, lto ? "wrong-width-lto" : "wrong-width-object");
        fixture.Produce("int native_probe(void) { return 42; }", target == "wasm64" ? "wasm32" : "wasm64", lto, false);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());

        Assert.Contains("A NetWasm runtime tool failed", error.Message, StringComparison.Ordinal);
        fixture.AssertUnpublished();
        var run = Assert.Single(Directory.GetDirectories(fixture.Request.LogDirectory, "native-run-*"));
        Assert.True(File.Exists(Path.Combine(run, "native-archive-validation-0.log")));
        Assert.False(File.Exists(Path.Combine(run, "native-probe-link.log")));
        fixture.WriteReceipt("archive-width");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        private readonly CommandInvoker _commands = new();
        private readonly string _producerTools;
        private readonly string _suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        private readonly string _cell;
        private readonly Dictionary<string, string> _inputs = new(StringComparer.Ordinal);
        public RuntimeMaterializationRequest Request { get; }

        public Fixture(string target, RuntimeWasmOptimization optimization, string kind)
        {
            var root = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT") ?? _temporary.Path;
            _cell = Path.Combine(root, target + "-" + optimization + "-" + kind);
            Assert.False(Directory.Exists(_cell));
            Directory.CreateDirectory(_cell);
            var repository = RepositoryRoot();
            var assetRoot = Path.Combine(repository, "src", "NetWasm.Runtime.Pack", "runtime");
            var emsdk = Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "emsdk");
            _producerTools = Path.Combine(emsdk, "upstream", "bin");
            var hostTools = RequiredDirectory("NETWASM_NATIVE_INTEROP_HOST_TOOLS_ROOT");
            var toolchain = RequiredDirectory("NETWASM_NATIVE_INTEROP_TOOLCHAIN_ROOT");
            var layout = Path.Combine(_cell, "runtime-layout.json");
            File.WriteAllText(layout, JsonSerializer.Serialize(new
            {
                schemaVersion = 3,
                target,
                applicationStaticDataEnd = 65_537,
                nativeImports = new[] { new { libraryName = "probe", entryPoint = "native_probe", parameters = Array.Empty<string>(), returnType = "i32" } },
            }));
            Request = new(Path.Combine(assetRoot, "runtime-pack.json"), layout, assetRoot,
                Path.Combine(hostTools, "tools", "bin", "wasm-ld" + _suffix),
                Path.Combine(hostTools, "tools", "bin", "wasm-opt" + _suffix),
                Path.Combine(hostTools, "tools", "bin", "node" + _suffix),
                Path.Combine(toolchain, "tools", "wasm-tools", "run-wasm-tools.mjs"),
                Path.Combine(toolchain, "tools", "wasm-tools", "wasm-tools.wasm"),
                Path.Combine(_cell, "runtime.wasm"), Path.Combine(_cell, "logs"), Path.Combine(_cell, "cache"),
                target, optimization, 65_537, null,
                new("source-test", "source-test", "source-test", "source-test", "qualified-host-tools", "0.5.0",
                    "emscripten-6.0.7", "emscripten-6.0.7", "qualified-wasm-tools", "qualified-node"))
            { NativeLibraries = [new("probe", target, Path.Combine(_cell, "libprobe.a"))] };
            foreach (var input in new[] { Request.ManifestPath, Path.Combine(assetRoot, "runtime-policy.json"),
                typeof(RuntimeMaterializationComposition).Assembly.Location, typeof(NativeRuntimeValidationRejectionIntegrationTests).Assembly.Location })
            {
                var copy = Path.Combine(_cell, Path.GetFileName(input));
                File.Copy(input, copy);
                _inputs.Add(Path.GetFileName(input), new Sha256ArtifactDigestCalculator().Calculate(copy));
            }
        }

        public void Produce(string contents, string actualTarget, bool lto, bool simd)
        {
            var source = Path.Combine(_cell, "probe.c");
            File.WriteAllText(source, contents);
            var objectPath = Path.Combine(_cell, "probe.o");
            var arguments = ImmutableArray.Create("--target=" + actualTarget + "-unknown-emscripten", "-O2", "-ffreestanding",
                "-ffunction-sections", "-fdata-sections", "-c", source, "-o", objectPath);
            if (lto) arguments = arguments.Add("-flto");
            if (simd) arguments = arguments.Add("-msimd128");
            _commands.Invoke(new(Path.Combine(_producerTools, "clang" + _suffix), arguments, Path.Combine(_cell, "produce.log")));
            if (simd)
                _commands.Invoke(new(Path.Combine(_producerTools, "llvm-objcopy" + _suffix), ["--remove-section=target_features", objectPath],
                    Path.Combine(_cell, "remove-feature-metadata.log")));
            _commands.Invoke(new(Path.Combine(_producerTools, "llvm-ar" + _suffix), ["rcs", Request.NativeLibraries[0].Path, objectPath],
                Path.Combine(_cell, "archive.log")));
            _inputs.Add("probe.c", new Sha256ArtifactDigestCalculator().Calculate(source));
            _inputs.Add("probe.o", new Sha256ArtifactDigestCalculator().Calculate(objectPath));
        }

        public RuntimeMaterialization Materialize() => RuntimeMaterializationComposition.Create().Materialize(Request);

        public void AssertUnpublished()
        {
            Assert.False(File.Exists(Request.OutputPath));
            Assert.False(File.Exists(RuntimeMaterializationCachePaths.Entry(Request.CacheDirectory, new(Request.Target, Request.Optimization))));
            Assert.All(Directory.GetDirectories(Request.LogDirectory, "native-run-*"), run => Assert.False(Directory.Exists(Path.Combine(run, "work"))));
        }

        public void WriteReceipt(string stage) => File.WriteAllText(Path.Combine(_cell, "receipt.json"), JsonSerializer.Serialize(new
        {
            Target = Request.Target,
            Optimization = Request.Optimization.ToString(),
            RejectionStage = stage,
            Inputs = _inputs,
            Archive = new RuntimeNativeArchiveReader().Read(Request.NativeLibraries[0].Path),
            Published = File.Exists(Request.OutputPath),
        }, ReceiptOptions));

        public void Dispose() => _temporary.Dispose();

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
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(directory.FullName, "src", "NetWasm.Runtime.Pack")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("The integration test repository root is unavailable.");
        }
    }
}
