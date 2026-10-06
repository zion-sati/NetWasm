using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeModuleMaterializerTests
{
    [Theory]
    [InlineData("wasm32", null)]
    [InlineData("wasm64", null)]
    [InlineData("wasm32", "Boehm")]
    [InlineData("wasm64", "Boehm")]
    [InlineData("wasm32", "Compact")]
    [InlineData("wasm64", "Compact")]
    public void MaterializesAndValidatesTargetSpecificRuntime(string target, string? collector)
    {
        using var directory = new TemporaryDirectory();
        var manifest = RuntimePackTestData.Manifest();
        var sourceLayout = new RuntimeLayout(2, target, 65_537);
        var calculatedLayout = RuntimePackTestData.Layout(target);
        var manifests = new RecordingManifestReader(manifest);
        var layouts = new RecordingLayoutReader(sourceLayout);
        var calculator = new RecordingLayoutCalculator(calculatedLayout);
        var assets = new RecordingAssetVerifier();
        var arguments = new RecordingArgumentBuilder(["link"]);
        var optimization = new RecordingOptimizationArgumentBuilder(["optimize"]);
        var commands = new RecordingCommandInvoker();
        var materializer = Materializer(
            manifests,
            layouts,
            calculator,
            assets,
            arguments,
            optimization,
            commands,
            new Sha256ArtifactDigestCalculator());
        var request = Request(directory, target) with
        {
            GarbageCollector = collector,
            InitialHeapSizeBytes = 131_072,
            MaximumMemorySizeBytes = calculatedLayout.MaximumMemorySizeBytes,
        };

        var capability = Assert.IsAssignableFrom<IRuntimeModuleMaterializer>(materializer);
        var result = capability.Materialize(request);

        Assert.Equal(target, result.Target);
        Assert.Equal(collector ?? "Boehm", result.GarbageCollector);
        Assert.EndsWith($"{target}/{result.GarbageCollector.ToLowerInvariant()}/libnetwasm-runtime.a", assets.Verifications[0].Path,
            StringComparison.Ordinal);
        Assert.EndsWith($"{target}/{result.GarbageCollector.ToLowerInvariant()}/libgc.a", assets.Verifications[1].Path,
            StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(request.OutputPath), result.OutputPath);
        Assert.Equal(new Sha256ArtifactDigestCalculator().Calculate(request.OutputPath), result.Sha256);
        Assert.Equal(manifest.RuntimeAbi, result.RuntimeAbi);
        Assert.Equal(manifest.Provenance.BuildSeam, result.BuildSeam);
        Assert.Equal(manifest.Provenance.ToolchainFingerprint, result.ToolchainFingerprint);
        Assert.Equal(calculatedLayout.RuntimeGlobalBase, result.RuntimeGlobalBase);
        Assert.Equal(calculatedLayout.HeapBase, result.HeapBase);
        Assert.Equal(calculatedLayout.InitialMemorySizeBytes, result.InitialMemorySizeBytes);
        Assert.Equal(calculatedLayout.MaximumMemorySizeBytes, result.MaximumMemorySizeBytes);
        Assert.Equal(request.ManifestPath, manifests.Path);
        Assert.Equal(request.RuntimeLayoutPath, layouts.Path);
        Assert.Equal(request.InitialHeapSizeBytes, calculator.Request?.InitialHeapSizeBytes);
        Assert.Equal(request.MaximumMemorySizeBytes, calculator.Request?.MaximumMemorySizeBytes);
        Assert.Equal(4, assets.Verifications.Count);
        Assert.EndsWith($"{target}/system-libraries/libc.a", assets.Verifications[3].Path,
            StringComparison.Ordinal);
        Assert.Equal(request.OutputPath, arguments.Request?.OutputPath);
        Assert.Equal(3, commands.Commands.Count);
        var link = commands.Commands[0];
        Assert.Equal(request.WasmLdPath, link.ExecutablePath);
        Assert.Equal(["link"], link.Arguments.ToArray());
        Assert.EndsWith("runtime-link.log", link.LogPath, StringComparison.Ordinal);
        var optimize = commands.Commands[1];
        Assert.Equal(request.WasmOptPath, optimize.ExecutablePath);
        Assert.Equal(["optimize"], optimize.Arguments.ToArray());
        Assert.EndsWith("runtime-optimize.log", optimize.LogPath, StringComparison.Ordinal);
        var validation = commands.Commands[2];
        Assert.Equal(request.WasmToolsNodePath, validation.ExecutablePath);
        Assert.Equal("--disable-warning=ExperimentalWarning", validation.Arguments[0]);
        Assert.Equal(request.WasmToolsCommandPath, validation.Arguments[1]);
        Assert.Equal(request.WasmToolsModulePath, validation.Arguments[2]);
        Assert.Equal("validate", validation.Arguments[3]);
        Assert.Equal(Path.GetFullPath(request.OutputPath), validation.Arguments[4]);
        Assert.EndsWith("runtime-validate.log", validation.LogPath, StringComparison.Ordinal);
    }

    [Fact]
    public void NoneSkipsRuntimeOptimization()
    {
        using var directory = new TemporaryDirectory();
        var commands = new RecordingCommandInvoker();
        var materializer = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 0)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            commands,
            new Sha256ArtifactDigestCalculator());

        materializer.Materialize(Request(directory, "wasm32") with
        {
            Optimization = RuntimeWasmOptimization.None,
            WasmOptPath = string.Empty,
        });

        Assert.Equal(2, commands.Commands.Count);
        Assert.DoesNotContain(commands.Commands,
            command => command.LogPath.EndsWith("runtime-optimize.log", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsLayoutTargetMismatch()
    {
        using var directory = new TemporaryDirectory();
        var materializer = Create(new RuntimeLayout(2, "wasm64", 0));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Request(directory, "wasm32")));
        Assert.Equal("The NetWasm runtime layout target does not match the requested target.", exception.Message);
    }

    [Fact]
    public void CallbackOnlyLayoutSelectsNativeMaterialization()
    {
        using var directory = new TemporaryDirectory();
        var callbackSupport = RuntimePackTestData.CallbackSupport();
        var native = new RecordingNativeMaterializer();
        var materializer = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(4, "wasm32", 65_537)
            {
                NativeCallbackSupport = callbackSupport,
            }),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            new RecordingCommandInvoker(),
            new Sha256ArtifactDigestCalculator(),
            nativeModules: native);
        var request = Request(directory, "wasm32") with
        {
            NativeCallbackObjectPath = directory.PathTo(
                "application.callbacks.o"),
        };

        var result = materializer.Materialize(request);

        Assert.Same(native.Result, result);
        Assert.Same(callbackSupport,
            native.Request?.SourceLayout.NativeCallbackSupport);
        Assert.Empty(native.Request!.SourceLayout.NativeImports);
    }

    [Fact]
    public void RejectsUnavailableTarget()
    {
        using var directory = new TemporaryDirectory();
        var materializer = Create(new RuntimeLayout(2, "wasm128", 0));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Request(directory, "wasm128")));
        Assert.Equal("The requested NetWasm runtime target and collector pair is unavailable.", exception.Message);
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        using var directory = new TemporaryDirectory();
        var materializer = Create(new RuntimeLayout(2, "wasm32", 0));
        Assert.Throws<ArgumentNullException>(() => materializer.Materialize(null!));
        Assert.Throws<ArgumentException>(() => materializer.Materialize(
            Request(directory, "wasm32") with { ManifestPath = "" }));
    }

    [Fact]
    public void RejectsUndefinedOptimizationBeforeInvokingCommands()
    {
        using var directory = new TemporaryDirectory();
        var commands = new RecordingCommandInvoker();
        var materializer = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 0)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            commands,
            new Sha256ArtifactDigestCalculator());

        Assert.Throws<ArgumentOutOfRangeException>(() => materializer.Materialize(
            Request(directory, "wasm32") with
            {
                Optimization = (RuntimeWasmOptimization)42,
            }));
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public void RejectsIncompletePackagedSystemLibraryClosure()
    {
        using var directory = new TemporaryDirectory();
        var target = RuntimePackTestData.Target("wasm32") with
        {
            SystemLibraries = new(["libc.a"], []),
        };
        var manifest = RuntimePackTestData.Manifest() with
        {
            TargetLookup = RuntimePackTestData.Manifest().TargetLookup.SetItem((target.Target, target.GarbageCollector), target),
        };
        var commands = new RecordingCommandInvoker();
        var materializer = Materializer(
            new RecordingManifestReader(manifest),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 0)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            commands,
            new Sha256ArtifactDigestCalculator());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Request(directory, "wasm32")));

        Assert.Equal(
            "The packaged Emscripten system-library closure is incomplete.",
            exception.Message);
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public void SameDerivedLayoutUsesCacheAndSkipsAllRuntimeTools()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32");
        var bytes = File.ReadAllBytes(request.OutputPath);
        var coldCommands = new RecordingCommandInvoker();
        var cold = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            coldCommands,
            new Sha256ArtifactDigestCalculator());

        var coldResult = cold.Materialize(request);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, coldResult.CacheMetrics.Outcome);
        Assert.True(coldResult.CacheMetrics.Recomputed);
        Assert.Equal(3, coldCommands.Commands.Count);
        File.Delete(request.OutputPath);

        var warmCommands = new RecordingCommandInvoker();
        var warm = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_545)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            warmCommands,
            new Sha256ArtifactDigestCalculator());

        var warmResult = warm.Materialize(request);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, warmResult.CacheMetrics.Outcome);
        Assert.False(warmResult.CacheMetrics.Recomputed);
        Assert.Empty(warmCommands.Commands);
        Assert.Equal(bytes, File.ReadAllBytes(request.OutputPath));
        Assert.Equal(coldResult.Sha256, warmResult.Sha256);
    }

    [Fact]
    public void DerivedLayoutBoundaryForcesRecomputation()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32");
        var first = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            new RecordingCommandInvoker(),
            new Sha256ArtifactDigestCalculator());
        first.Materialize(request);
        var commands = new RecordingCommandInvoker();
        var changedLayout = RuntimePackTestData.Layout() with { RuntimeGlobalBase = 65_568 };
        var second = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_553)),
            new RecordingLayoutCalculator(changedLayout),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            commands,
            new Sha256ArtifactDigestCalculator());

        var result = second.Materialize(request);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, result.CacheMetrics.Outcome);
        Assert.True(result.CacheMetrics.Recomputed);
        Assert.Equal(3, commands.Commands.Count);
    }

    [Fact]
    public void ChangedLayoutsReplaceOneTargetModeSlot()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32");

        foreach (var runtimeGlobalBase in new long[] { 65_536, 131_072, 196_608, 262_144 })
        {
            var layout = RuntimePackTestData.Layout() with
            {
                RuntimeGlobalBase = runtimeGlobalBase,
                HeapBase = runtimeGlobalBase + 65_536,
            };
            var result = Materializer(
                new RecordingManifestReader(RuntimePackTestData.Manifest()),
                new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", runtimeGlobalBase - 1)),
                new RecordingLayoutCalculator(layout),
                new RecordingAssetVerifier(),
                new RecordingArgumentBuilder(["link"]),
                new RecordingOptimizationArgumentBuilder(["optimize"]),
                new RecordingCommandInvoker(),
                new Sha256ArtifactDigestCalculator()).Materialize(request);

            Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, result.CacheMetrics.Outcome);
            Assert.True(result.CacheMetrics.Recomputed);
            Assert.Single(Directory.EnumerateFiles(
                request.CacheDirectory,
                "*.nwcache",
                SearchOption.AllDirectories));
        }
    }

    [Fact]
    public void FailedReplacementPreservesThePreviousSlot()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32");
        var originalLayout = RuntimePackTestData.Layout();
        Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(originalLayout),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            new RecordingCommandInvoker(),
            new Sha256ArtifactDigestCalculator()).Materialize(request);

        var changedLayout = originalLayout with { RuntimeGlobalBase = 131_072 };
        Assert.Throws<InvalidOperationException>(() => Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 131_071)),
            new RecordingLayoutCalculator(changedLayout),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            new ThrowingCommandInvoker(),
            new Sha256ArtifactDigestCalculator()).Materialize(request));

        var warmCommands = new RecordingCommandInvoker();
        var result = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(originalLayout),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            warmCommands,
            new Sha256ArtifactDigestCalculator()).Materialize(request);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, result.CacheMetrics.Outcome);
        Assert.False(result.CacheMetrics.Recomputed);
        Assert.Empty(warmCommands.Commands);
        Assert.Single(Directory.EnumerateFiles(
            request.CacheDirectory,
            "*.nwcache",
            SearchOption.AllDirectories));
    }

    [Fact]
    public void CorruptEntryIsRecomputedAndReplaced()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32");
        var cold = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            new RecordingCommandInvoker(),
            new Sha256ArtifactDigestCalculator());
        cold.Materialize(request);
        var cacheEntry = Assert.Single(Directory.EnumerateFiles(
            request.CacheDirectory,
            "*.nwcache",
            SearchOption.AllDirectories));
        File.WriteAllBytes(cacheEntry, [0]);
        var commands = new RecordingCommandInvoker();
        var retry = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            commands,
            new Sha256ArtifactDigestCalculator());

        var result = retry.Materialize(request);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.CacheMetrics.Outcome);
        Assert.True(result.CacheMetrics.Recomputed);
        Assert.Equal(3, commands.Commands.Count);
        Assert.Equal(
            RuntimeMaterializationCacheOutcome.Hit,
            new RuntimeMaterializationCacheReader().Read(
                request.CacheDirectory,
                new RuntimeMaterializationCacheSlot("wasm32", RuntimeWasmOptimization.Oz),
                CacheKeyFor(
                    request,
                    RuntimePackTestData.Manifest(),
                    RuntimePackTestData.Layout())).Outcome);
    }

    [Theory]
    [MemberData(nameof(OptionalCacheFailures))]
    public void CachePersistenceFailureKeepsValidatedRuntime(Exception failure)
    {
        using var directory = new TemporaryDirectory();
        var commands = new RecordingCommandInvoker();
        var materializer = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            commands,
            new Sha256ArtifactDigestCalculator(),
            cacheWriter: new ThrowingCacheWriter(failure));
        var request = Request(directory, "wasm32");

        var result = materializer.Materialize(request);

        Assert.True(File.Exists(request.OutputPath));
        Assert.Equal(new Sha256ArtifactDigestCalculator().Calculate(request.OutputPath), result.Sha256);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, result.CacheMetrics.Outcome);
        Assert.True(result.CacheMetrics.Recomputed);
        Assert.Equal(3, commands.Commands.Count);
    }

    [Fact]
    public void CacheInvariantFailureRemainsObservable()
    {
        using var directory = new TemporaryDirectory();
        var expected = new InvalidOperationException("cache invariant");
        var materializer = Materializer(
            new RecordingManifestReader(RuntimePackTestData.Manifest()),
            new RecordingLayoutReader(new RuntimeLayout(2, "wasm32", 65_537)),
            new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
            new RecordingAssetVerifier(),
            new RecordingArgumentBuilder(["link"]),
            new RecordingOptimizationArgumentBuilder(["optimize"]),
            new RecordingCommandInvoker(),
            new Sha256ArtifactDigestCalculator(),
            cacheWriter: new ThrowingCacheWriter(expected));

        var actual = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Request(directory, "wasm32")));

        Assert.Same(expected, actual);
    }

    public static TheoryData<Exception> OptionalCacheFailures() => new()
    {
        new IOException("cache unavailable"),
        new UnauthorizedAccessException("cache denied"),
    };

    private static RuntimeModuleMaterializer Create(RuntimeLayout layout) => Materializer(
        new RecordingManifestReader(RuntimePackTestData.Manifest()),
        new RecordingLayoutReader(layout),
        new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
        new RecordingAssetVerifier(),
        new RecordingArgumentBuilder(["link"]),
        new RecordingOptimizationArgumentBuilder(["optimize"]),
        new RecordingCommandInvoker(),
        new Sha256ArtifactDigestCalculator());

    private static RuntimeModuleMaterializer Materializer(
        IRuntimePackManifestReader manifests,
        IRuntimeLayoutReader layouts,
        IRuntimeMemoryLayoutCalculator memoryLayouts,
        IRuntimeAssetDigestVerifier assetDigests,
        IRuntimeLinkArgumentBuilder linkArguments,
        IRuntimeOptimizationArgumentBuilder optimizationArguments,
        ICommandInvoker commands,
        IArtifactDigestCalculator artifactDigests,
        IRuntimeMaterializationCacheWriter? cacheWriter = null,
        IRuntimeNativeModuleMaterializer? nativeModules = null) =>
        new(
            manifests,
            new RuntimePackTargetSelector(),
            layouts,
            memoryLayouts,
            assetDigests,
            linkArguments,
            optimizationArguments,
            new RuntimeMaterializationCacheKeyBuilder(),
            new RuntimeMaterializationCacheReader(),
            cacheWriter ?? new RuntimeMaterializationCacheWriter(),
            new RuntimeArtifactPublisher(new Sha256ArtifactDigestCalculator()),
            commands,
            artifactDigests,
            nativeModules ?? new RejectingNativeMaterializer());

    private sealed class RejectingNativeMaterializer : IRuntimeNativeModuleMaterializer
    {
        public RuntimeMaterialization Materialize(RuntimeNativeMaterializationRequest request) =>
            throw new InvalidOperationException("Unexpected native materialization in runtime-only test.");
    }

    private sealed class RecordingNativeMaterializer : IRuntimeNativeModuleMaterializer
    {
        public RuntimeMaterialization Result { get; } = new(
            "wasm32",
            "/runtime.wasm",
            RuntimePackTestData.Digest,
            "netwasm.runtime.v1",
            "test",
            "fingerprint",
            0,
            0,
            0,
            65_536,
            new("runtime-materialization", "0123456789ab",
                RuntimeMaterializationCacheOutcome.Miss, true, 0, 0, 0));

        public RuntimeNativeMaterializationRequest? Request { get; private set; }

        public RuntimeMaterialization Materialize(
            RuntimeNativeMaterializationRequest request)
        {
            Request = request;
            return Result;
        }
    }

    private static RuntimeMaterializationRequest Request(TemporaryDirectory directory, string target)
    {
        directory.WriteBytes("output/runtime.wasm", [0, 97, 115, 109]);
        return new(
            directory.PathTo("runtime-pack.json"),
            directory.PathTo("runtime-layout.json"),
            directory.Path,
            directory.PathTo("wasm-ld"),
            directory.PathTo("wasm-opt"),
            directory.PathTo("node"),
            directory.PathTo("run-wasm-tools.mjs"),
            directory.PathTo("wasm-tools.wasm"),
            directory.PathTo("output/runtime.wasm"),
            directory.PathTo("logs"),
            directory.PathTo("cache"),
            target,
            RuntimeWasmOptimization.Oz,
            null,
            null,
            new(
                "sdk-version",
                "compiler-version",
                "runtime-version",
                "runtime-pack-version",
                "host-tools",
                "host-tools-version",
                "wasm-ld-version",
                "wasm-opt-version",
                "wasm-tools-version",
                "node-version"));
    }

    private sealed class RecordingOptimizationArgumentBuilder(ImmutableArray<string> result) :
        IRuntimeOptimizationArgumentBuilder
    {
        public RuntimeOptimizationRequest? Request { get; private set; }

        public ImmutableArray<string> Build(RuntimeOptimizationRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingManifestReader(RuntimePackManifest result) : IRuntimePackManifestReader
    {
        public string? Path { get; private set; }

        public RuntimePackManifest Read(string path)
        {
            Path = path;
            return result;
        }
    }

    private sealed class RecordingLayoutReader(RuntimeLayout result) : IRuntimeLayoutReader
    {
        public string? Path { get; private set; }

        public RuntimeLayout Read(string path)
        {
            Path = path;
            return result;
        }
    }

    private sealed class RecordingLayoutCalculator(RuntimeMemoryLayout result) : IRuntimeMemoryLayoutCalculator
    {
        public RuntimeMemoryLayoutRequest? Request { get; private set; }

        public RuntimeMemoryLayout Calculate(RuntimeMemoryLayoutRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingAssetVerifier : IRuntimeAssetDigestVerifier
    {
        public List<(string Path, string Digest)> Verifications { get; } = [];

        public void Verify(string path, string expectedSha256) =>
            Verifications.Add((path, expectedSha256));
    }

    private sealed class RecordingArgumentBuilder(ImmutableArray<string> result) : IRuntimeLinkArgumentBuilder
    {
        public RuntimeLinkRequest? Request { get; private set; }

        public ImmutableArray<string> Build(RuntimeLinkRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingCommandInvoker : ICommandInvoker
    {
        public List<RuntimeCommand> Commands { get; } = [];

        public void Invoke(RuntimeCommand command) => Commands.Add(command);
    }

    private sealed class ThrowingCommandInvoker : ICommandInvoker
    {
        public void Invoke(RuntimeCommand command) =>
            throw new InvalidOperationException("runtime command failed");
    }

    private sealed class ThrowingCacheWriter(Exception failure) : IRuntimeMaterializationCacheWriter
    {
        public void Write(
            string cacheDirectory,
            RuntimeMaterializationCacheSlot slot,
            RuntimeMaterializationCacheKey key,
            byte[] bytes,
            string sha256,
            RuntimeNativeCacheEvidence? nativeEvidence = null) => throw failure;
    }

    private static RuntimeMaterializationCacheKey CacheKeyFor(
        RuntimeMaterializationRequest request,
        RuntimePackManifest manifest,
        RuntimeMemoryLayout layout) =>
        new RuntimeMaterializationCacheKeyBuilder().Build(new RuntimeMaterializationCacheKeyRequest(
            request.BuildIdentity,
            manifest,
            manifest.Targets.Single(target => target.Target == request.Target &&
                target.GarbageCollector == (request.GarbageCollector ?? manifest.DefaultGarbageCollector)),
            layout,
            request.Optimization,
            ["link"],
            ["optimize"],
            request.AssetRoot,
            request.OutputPath));

}
