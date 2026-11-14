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
            "wrong-width" => (Module: otherPrefix + "|wasi:cli/exit@0.3", Name: "exit"),
            "wrong-signature" => (Module: (target == "wasm64" ? "cm64p2" : "cm32p2") + "|wasi:cli/exit@0.3", Name: "exit"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var source = $$"""
            __attribute__((import_module("{{identity.Module}}"), import_name("{{identity.Name}}")))
            extern int native_dependency(int);
            int native_probe(void) { return native_dependency(7); }
            """;
        fixture.Produce(source, target, false);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());

        // An import outside the contract is named; only a module the project may
        // declare suggests NetWasmImportModule.
        if (kind == "wrong-signature")
            Assert.Equal("The native-linked runtime imports do not match the supported runtime contract.", error.Message);
        else
            Assert.StartsWith($"The native-linked runtime imports {identity.Module}.{identity.Name}, ", error.Message, StringComparison.Ordinal);
        Assert.Equal(kind == "arbitrary-host", error.Message.Contains("NetWasmImportModule", StringComparison.Ordinal));
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
        // Tail calls are outside the static-native profile; clang emits return_call for the sibling call.
        using var fixture = new Fixture(target, optimization, "unsupported-feature");
        fixture.Produce("""
            __attribute__((noinline)) int native_leaf(void) { return 42; }
            int native_probe(void) { __attribute__((musttail)) return native_leaf(); }
            """, target, false, "-mtail-call");

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());

        Assert.StartsWith("Native library 'probe' uses WebAssembly beyond NetWasm's static native profile (mvp, ", error.Message,
            StringComparison.Ordinal);
        Assert.Contains("simd", error.Message, StringComparison.Ordinal);
        Assert.Contains("tail calls support is not enabled", error.Message, StringComparison.Ordinal);
        fixture.AssertUnpublished();
        var run = Assert.Single(Directory.GetDirectories(fixture.Request.LogDirectory, "native-run-*"));
        Assert.True(File.Exists(Path.Combine(run, "native-archive-features-0.log")));
        Assert.False(File.Exists(Path.Combine(run, "native-final-link.log")));
        fixture.WriteReceipt("feature-profile");
    }

    // The same archive the profile rejected for tail calls is accepted with Wasm SIMD,
    // which the application module itself emits.
    [Theory]
    [InlineData("wasm32", RuntimeWasmOptimization.None)]
    [InlineData("wasm32", RuntimeWasmOptimization.Oz)]
    [InlineData("wasm64", RuntimeWasmOptimization.None)]
    [InlineData("wasm64", RuntimeWasmOptimization.Oz)]
    [Trait("Category", "NativeValidationRejectionIntegration")]
    public void AcceptsSimdEvenWithoutTargetFeatureMetadata(string target, RuntimeWasmOptimization optimization)
    {
        using var fixture = new Fixture(target, optimization, "simd");
        fixture.Produce("""
            typedef unsigned vector4 __attribute__((vector_size(16)));
            volatile vector4 native_values;
            int native_probe(void) {
                vector4 next = native_values + (vector4){1, 2, 3, 4};
                native_values = next;
                return (int)next[0];
            }
            """, target, false, "-msimd128");

        var materialization = fixture.Materialize();

        Assert.True(File.Exists(fixture.Request.OutputPath));
        Assert.Equal(new Sha256ArtifactDigestCalculator().Calculate(fixture.Request.OutputPath), materialization.Sha256);
        fixture.WriteReceipt("simd");
    }

    // Siblings of issue 200, an archive with Wasm SIMD that failed as a tool failure: every other feature clang
    // emits from C, without its target_features metadata. One inside the static native profile links; one
    // outside it is refused with the library, the profile and the validator's reason, before any link.
    public static TheoryData<string, string, string, string?> NativeFeatures => new()
    {
        { "sign-extension", "-msign-ext", "int native_probe(void) { volatile int value = 200; return (signed char)value; }", null },
        { "saturating-float-to-int", "-mnontrapping-fptoint",
            "int native_probe(void) { volatile float value = 3.5e10f; return (int)value; }", null },
        { "bulk-memory", "-mbulk-memory", """
            volatile char native_source[64];
            char native_target[64];
            int native_probe(void) {
                volatile int size = 64;
                __builtin_memcpy(native_target, (const char *)native_source, size);
                __builtin_memset(native_target, 7, size / 2);
                return native_target[3];
            }
            """, null },
        { "threads", "-matomics -mbulk-memory",
            "int native_probe(void) { static int counter; return __atomic_fetch_add(&counter, 1, __ATOMIC_SEQ_CST); }",
            "threads support is not enabled" },
        { "relaxed-simd", "-msimd128 -mrelaxed-simd", """
            typedef float f32x4 __attribute__((vector_size(16)));
            volatile f32x4 native_values;
            int native_probe(void) {
                f32x4 next = __builtin_wasm_relaxed_madd_f32x4(native_values, native_values, native_values);
                native_values = next;
                return (int)next[0];
            }
            """, "relaxed SIMD support is not enabled" },
        { "exceptions", "-mexception-handling", """
            static int native_thrown;
            int native_probe(void) { __builtin_wasm_throw(0, &native_thrown); }
            """, "exceptions" },
    };

    [Theory]
    [MemberData(nameof(NativeFeatures))]
    [Trait("Category", "NativeValidationRejectionIntegration")]
    public void EachWasmFeatureLinksInsideTheProfileOrIsNamedOutsideIt(string name, string flags, string source, string? reason)
    {
        foreach (var target in new[] { "wasm32", "wasm64" })
        {
            using var fixture = new Fixture(target, RuntimeWasmOptimization.Oz, "feature-" + name);
            fixture.Produce(source, target, false, flags);

            if (reason is null)
            {
                var materialization = fixture.Materialize();
                Assert.Equal(new Sha256ArtifactDigestCalculator().Calculate(fixture.Request.OutputPath), materialization.Sha256);
                fixture.WriteReceipt("feature-" + name);
                continue;
            }
            var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());
            Assert.StartsWith("Native library 'probe' uses WebAssembly beyond NetWasm's static native profile (mvp, ",
                error.Message, StringComparison.Ordinal);
            Assert.Contains(reason, error.Message, StringComparison.Ordinal);
            fixture.AssertUnpublished();
            fixture.WriteReceipt("feature-" + name);
        }
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
        fixture.Produce("int native_probe(void) { return 42; }", target == "wasm64" ? "wasm32" : "wasm64", lto);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Materialize());

        Assert.Contains(") failed with exit code ", error.Message, StringComparison.Ordinal);
        fixture.AssertUnpublished();
        var run = Assert.Single(Directory.GetDirectories(fixture.Request.LogDirectory, "native-run-*"));
        Assert.True(File.Exists(Path.Combine(run, "native-archive-validation-0.log")));
        Assert.False(File.Exists(Path.Combine(run, "native-probe-link.log")));
        fixture.WriteReceipt("archive-width");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();
        private readonly CommandInvoker _commands = new(new ToolOutputExcerptFormatter());
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
                runtimeFeatures = Array.Empty<string>(),
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

        // feature, when given, is the clang flags for a Wasm feature, space-separated; its target_features metadata
        // is then removed, so only the instructions themselves can reveal it.
        public void Produce(string contents, string actualTarget, bool lto, string? feature = null)
        {
            var source = Path.Combine(_cell, "probe.c");
            File.WriteAllText(source, contents);
            var objectPath = Path.Combine(_cell, "probe.o");
            var arguments = ImmutableArray.Create("--target=" + actualTarget + "-unknown-emscripten", "-O2", "-ffreestanding",
                "-ffunction-sections", "-fdata-sections", "-c", source, "-o", objectPath);
            if (lto) arguments = arguments.Add("-flto");
            if (feature is not null) arguments = arguments.AddRange(feature.Split(' '));
            _commands.Invoke(new(Path.Combine(_producerTools, "clang" + _suffix), arguments, Path.Combine(_cell, "produce.log")));
            if (feature is not null)
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
