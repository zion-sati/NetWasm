using System.Collections.Immutable;
using System.Security.Cryptography;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeModuleMaterializerTests
{
    [Fact]
    public void CallbackOnlyMaterializationUsesOwnedCompilerSupportWithoutProviders()
    {
        var fixture = new Fixture();
        var request = CallbackRequest(fixture.Bytes);

        var result = fixture.Create().Materialize(request);

        Assert.Null(fixture.ResolutionRequest);
        Assert.Equal(0, fixture.ArchiveChecks);
        Assert.Empty(Assert.IsType<RuntimeNativeMaterializationCacheKeyRequest>(
            fixture.NativeKeyRequest).Bindings);
        Assert.NotNull(fixture.NativeKeyRequest.NativeCallbackSupport);
        Assert.Equal(
            "/input/application.callbacks.o",
            fixture.NativeKeyRequest.NativeCallbackObjectPath);
        Assert.Equal(
            "/input/application.callbacks.o.allow-undefined",
            fixture.NativeKeyRequest.NativeCallbackAllowedUndefinedPath);
        Assert.Equal(3, fixture.LinkRequests.Count);
        Assert.Equal(
            "/input/application.callbacks.o",
            fixture.LinkRequests[0].NativeCallbackObjectPath);
        Assert.All(fixture.LinkRequests.Skip(1), link =>
        {
            Assert.Empty(link.NativeBindings);
            Assert.Equal(
                Path.Combine(fixture.Workspace.DirectoryPath,
                    "application.callbacks.o"),
                link.NativeCallbackObjectPath);
            Assert.Equal(
                Path.Combine(fixture.Workspace.DirectoryPath,
                    "combined.allow-undefined"),
                link.NativeCallbackAllowedUndefinedPath);
        });
        Assert.Contains(
            Path.Combine(fixture.Workspace.DirectoryPath,
                "application.callbacks.o"),
            fixture.PublishedPaths);
        Assert.Equal(
            "runtime_symbol\n__netwasm_native_callback_0\n",
            System.Text.Encoding.UTF8.GetString(fixture.PublishedArtifacts[
                Path.Combine(fixture.Workspace.DirectoryPath,
                    "combined.allow-undefined")]));
        Assert.Equal(
            [new RuntimeLinkExport("__netwasm_application_callback_0", 0)],
            result.InternalApplicationExports.ToArray());
    }

    [Fact]
    public void StaticImportsExcludeProspectiveCallbackPathFromCacheIdentity()
    {
        var fixture = new Fixture();
        var baseline = Request();
        var request = baseline with
        {
            Consumer = baseline.Consumer with
            {
                NativeCallbackObjectPath = "/input/prospective.callbacks.o",
            },
        };

        fixture.Create().Materialize(request);

        Assert.Null(fixture.NativeKeyRequest!.NativeCallbackSupport);
        Assert.Null(fixture.NativeKeyRequest.NativeCallbackObjectPath);
        Assert.Null(fixture.NativeKeyRequest.NativeCallbackAllowedUndefinedPath);
        Assert.All(fixture.LinkRequests, link =>
        {
            Assert.Null(link.NativeCallbackObjectPath);
            Assert.Null(link.NativeCallbackAllowedUndefinedPath);
        });
    }

    [Fact]
    public void StaticImportsRejectExistingCallbackObjectWithoutLayoutAuthority()
    {
        using var directory = new TemporaryDirectory();
        var fixture = new Fixture();
        var callbackPath = Path.Combine(directory.Path, "prospective.callbacks.o");
        File.WriteAllBytes(callbackPath, fixture.Bytes);
        var baseline = Request();
        var request = baseline with
        {
            Consumer = baseline.Consumer with
            {
                NativeCallbackObjectPath = callbackPath,
            },
        };

        AssertRejectedBeforeWork(fixture, request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/input/wrong.callbacks.o")]
    public void CallbackSupportRejectsMissingOrMisnamedObjectBeforeWork(
        string? callbackPath)
    {
        var fixture = new Fixture();
        var baseline = CallbackRequest(fixture.Bytes);
        var request = baseline with
        {
            Consumer = baseline.Consumer with
            {
                NativeCallbackObjectPath = callbackPath,
            },
        };

        AssertRejectedBeforeWork(fixture, request);
    }

    [Fact]
    public void CallbackSupportRejectsObjectDigestMismatchBeforeWork()
    {
        var fixture = new Fixture();
        var baseline = CallbackRequest(fixture.Bytes);
        var request = baseline with
        {
            SourceLayout = baseline.SourceLayout with
            {
                NativeCallbackSupport = baseline.SourceLayout.NativeCallbackSupport! with
                {
                    Sha256 = new string('0', 64),
                },
            },
        };

        AssertRejectedBeforeWork(fixture, request);
    }

    [Theory]
    [InlineData("", "__netwasm_native_callback_0\n")]
    [InlineData("runtime_symbol", "runtime_symbol\n__netwasm_native_callback_0\n")]
    public void CallbackAllowListNormalizesEmptyAndUnterminatedRuntimeSymbols(
        string runtimeSymbols,
        string expected)
    {
        var fixture = new Fixture
        {
            RuntimeAllowedSymbols = System.Text.Encoding.UTF8.GetBytes(
                runtimeSymbols),
        };

        fixture.Create().Materialize(CallbackRequest(fixture.Bytes));

        Assert.Equal(
            expected,
            System.Text.Encoding.UTF8.GetString(fixture.PublishedArtifacts[
                Path.Combine(fixture.Workspace.DirectoryPath,
                    "combined.allow-undefined")]));
    }

    [Theory]
    [InlineData(RuntimeWasmOptimization.Oz, 5)]
    [InlineData(RuntimeWasmOptimization.None, 4)]
    public void LinksObservedLayoutTwiceAndPublishesOnlyValidatedBytes(RuntimeWasmOptimization optimization, int commandCount)
    {
        var fixture = new Fixture();
        var request = Request(optimization);

        var result = fixture.Create().Materialize(request);

        Assert.Equal(commandCount, fixture.Commands.Count);
        Assert.Equal(3, fixture.LinkRequests.Count); // identity, probe, exact-size final link
        Assert.Null(fixture.LinkRequests[0].Layout.InitialMemorySizeBytes);
        Assert.Null(fixture.LinkRequests[1].Layout.InitialMemorySizeBytes);
        Assert.Equal(fixture.Expected.InitialMemorySizeBytes, fixture.LinkRequests[2].Layout.InitialMemorySizeBytes);
        Assert.Equal("/owned/native-link/runtime.wasm", fixture.LinkRequests[1].OutputPath);
        Assert.Equal(request.Consumer.OutputPath, fixture.NativeKeyRequest!.OutputPath);
        Assert.Equal(fixture.Bindings, fixture.NativeKeyRequest.Bindings);
        Assert.Equal(fixture.Plan, fixture.NativeKeyRequest.Plan);
        Assert.Equal(request.SourceLayout.NativeImports, fixture.ResolutionRequest!.Imports);
        Assert.Equal(request.Consumer.NativeLibraries, fixture.ResolutionRequest.Providers);
        Assert.Equal(3, fixture.ModulesRead);
        Assert.Equal(3, fixture.ImportChecks);
        Assert.Equal(request.Target.NativeValidation, Assert.Single(fixture.ValidationRequests).Profile);
        Assert.Equal(3, fixture.TraceValidations.Count);
        Assert.Equal(2, fixture.LayoutChecks.Count);
        Assert.All(fixture.LayoutChecks, pair => Assert.Equal(fixture.Expected, pair.Expected));
        Assert.All(fixture.TraceValidations, validation =>
            Assert.Equal(["/owned/native-link/runtime.wasm.lto.o"], validation.PermittedGeneratedInputIdentities.ToArray()));
        Assert.Equal(1, fixture.ArchiveChecks);
        Assert.All(fixture.LinkRequests.Skip(1), link =>
            Assert.Equal("/owned/native-link/native-0.a", Assert.Single(link.NativeBindings).Provider.Path));
        Assert.All(fixture.Commands, command => Assert.StartsWith("/owned/native-logs/", command.LogPath, StringComparison.Ordinal));
        Assert.All(fixture.TraceValidations, validation =>
            Assert.Equal("/owned/native-link/native-0.a", Assert.Single(validation.Bindings).Provider.Path));
        Assert.Equal(fixture.Expected, fixture.WrittenEvidence!.Layout);
        Assert.Equal(fixture.Validated.ToArray(), fixture.WrittenEvidence.Bindings.ToArray());
        Assert.Equal(fixture.Bytes, fixture.PublishedBytes);
        Assert.Equal(Digest(fixture.Bytes), result.Sha256);
        Assert.Equal(result.Sha256, fixture.PublishedDigest);
        Assert.Equal(fixture.Expected.HeapBase, result.HeapBase);
        Assert.Equal(fixture.Expected.InitialMemorySizeBytes, result.InitialMemorySizeBytes);
        Assert.Equal([new RuntimeLinkExport("run", 0)], result.InternalRuntimeExports.ToArray());
        Assert.True(result.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, result.CacheMetrics.Outcome);
        Assert.True(fixture.Workspace.Disposed);
        Assert.True(fixture.Events.IndexOf("validate") < fixture.Events.IndexOf("cache-write"));
        Assert.True(fixture.Events.IndexOf("cache-write") < fixture.Events.IndexOf("publish"));
    }

    [Fact]
    public void WarmHitRevalidatesFrozenBoundsCurrentProviderEvidenceAndExportSignatures()
    {
        var fixture = new Fixture();
        fixture.Cached = new(RuntimeMaterializationCacheOutcome.Hit, fixture.Bytes, Digest(fixture.Bytes))
        { NativeEvidence = new(fixture.Expected, fixture.Validated) };

        var result = fixture.Create().Materialize(Request());

        Assert.Single(fixture.Commands);
        Assert.Equal(1, fixture.WorkspaceCreations);
        Assert.Equal(1, fixture.ImportChecks);
        Assert.Equal("/owned/native-link/cached-runtime.wasm", Assert.Single(fixture.ValidationRequests).Path);
        Assert.Equal(["/owned/native-link/cached-runtime.wasm", "/output/runtime.wasm"], fixture.PublishedPaths);
        Assert.True(fixture.Workspace.Disposed);
        Assert.Equal(1, fixture.ModulesRead);
        Assert.Equal(2, fixture.LayoutChecks.Count);
        var validation = Assert.Single(fixture.CachedValidations);
        Assert.Equal(fixture.Bindings, validation.Bindings);
        Assert.Equal(fixture.Validated, validation.Evidence);
        Assert.Null(fixture.WrittenEvidence);
        Assert.Equal(fixture.Bytes, fixture.PublishedBytes);
        Assert.False(result.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, result.CacheMetrics.Outcome);
        Assert.Equal([new RuntimeLinkExport("run", 0)], result.InternalRuntimeExports.ToArray());
    }

    [Theory]
    [InlineData(CacheDefect.MissingEvidence)]
    [InlineData(CacheDefect.MissingBytes)]
    [InlineData(CacheDefect.MissingDigest)]
    [InlineData(CacheDefect.InvalidModule)]
    [InlineData(CacheDefect.InvalidLayout)]
    [InlineData(CacheDefect.InvalidBindings)]
    [InlineData(CacheDefect.MissingLayout)]
    [InlineData(CacheDefect.DefaultBindings)]
    [InlineData(CacheDefect.NullBinding)]
    [InlineData(CacheDefect.InvalidImports)]
    public void InvalidNativeCacheEvidenceRecomputesWithoutPublishingCachedBytes(CacheDefect defect)
    {
        var fixture = new Fixture();
        fixture.Cached = new(RuntimeMaterializationCacheOutcome.Hit,
            defect == CacheDefect.MissingBytes ? null : [9], defect == CacheDefect.MissingDigest ? null : RuntimePackTestData.Digest)
        {
            NativeEvidence = defect == CacheDefect.MissingEvidence ? null : new(
            defect == CacheDefect.MissingLayout ? null! : fixture.Expected,
            defect switch { CacheDefect.DefaultBindings => default, CacheDefect.NullBinding => [null!], _ => fixture.Validated })
        };
        fixture.CacheDefect = defect;

        var result = fixture.Create().Materialize(Request());

        Assert.True(result.CacheMetrics.Recomputed);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.CacheMetrics.Outcome);
        Assert.Equal(5, fixture.Commands.Count);
        Assert.Equal(fixture.Bytes, fixture.PublishedBytes);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ToolFailureReleasesWorkspaceAndPublishesNothing(int commandNumber)
    {
        var fixture = new Fixture { FailCommand = commandNumber };

        Assert.Same(fixture.Failure, Assert.Throws<InvalidOperationException>(() => fixture.Create().Materialize(Request())));

        Assert.Equal(commandNumber, fixture.Commands.Count);
        Assert.Null(fixture.PublishedBytes);
        Assert.Null(fixture.WrittenEvidence);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Fact]
    public void WarmFeatureValidationFailureDoesNotRelinkOrPublishTheConsumerModule()
    {
        var fixture = new Fixture { FailCommand = 1 };
        fixture.Cached = new(RuntimeMaterializationCacheOutcome.Hit, fixture.Bytes, Digest(fixture.Bytes))
        { NativeEvidence = new(fixture.Expected, fixture.Validated) };

        Assert.Same(fixture.Failure, Assert.Throws<InvalidOperationException>(() => fixture.Create().Materialize(Request())));

        Assert.Single(fixture.Commands);
        Assert.Single(fixture.LinkRequests); // Key identity only, no link invocation.
        Assert.Equal(["/owned/native-link/cached-runtime.wasm"], fixture.PublishedPaths);
        Assert.Null(fixture.WrittenEvidence);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnsupportedImportsStopWithoutCachingOrPublishing(int moduleNumber)
    {
        var fixture = new Fixture { FailImportCheck = moduleNumber };

        Assert.Same(fixture.Failure, Assert.Throws<InvalidOperationException>(() => fixture.Create().Materialize(Request())));

        Assert.Equal(moduleNumber, fixture.ImportChecks);
        Assert.Null(fixture.WrittenEvidence);
        Assert.Empty(fixture.PublishedPaths);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Fact]
    public void ChangedArchiveStopsBeforeValidationLinkOrPublication()
    {
        var fixture = new Fixture { ChangedArchive = true };

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Create().Materialize(Request()));

        Assert.Contains("snapshot", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Commands);
        Assert.Equal(0, fixture.ModulesRead);
        Assert.Null(fixture.PublishedBytes);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Fact]
    public void MalformedProfileStopsBeforeProviderResolutionCacheOrToolWork()
    {
        var fixture = new Fixture { InvalidProfile = true };

        Assert.Same(fixture.Failure, Assert.Throws<InvalidOperationException>(() => fixture.Create().Materialize(Request())));

        Assert.Empty(fixture.Events);
        Assert.Empty(fixture.Commands);
        Assert.Equal(0, fixture.WorkspaceCreations);
        Assert.Empty(fixture.PublishedPaths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalCacheStorageFailureStillPublishesValidatedArtifact(bool unauthorized)
    {
        var fixture = new Fixture { CacheWriteFailure = unauthorized ? new UnauthorizedAccessException() : new IOException() };

        var result = fixture.Create().Materialize(Request());

        Assert.True(result.CacheMetrics.Recomputed);
        Assert.Equal(fixture.Bytes, fixture.PublishedBytes);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Fact]
    public void CacheInvariantFailureRemainsObservableAndPublishesNothing()
    {
        var failure = new InvalidOperationException("cache invariant");
        var fixture = new Fixture { CacheWriteFailure = failure };

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Create().Materialize(Request())));
        Assert.Null(fixture.PublishedBytes);
        Assert.True(fixture.Workspace.Disposed);
    }

    [Fact]
    public void RequiredNativeContextRejectsBeforeCollaboratorsRun()
    {
        var fixture = new Fixture();
        var materializer = fixture.Create();
        var request = Request();
        Assert.Throws<ArgumentNullException>(() => materializer.Materialize(null!));
        foreach (var invalid in new[]
        {
            request with { Consumer = null! }, request with { Manifest = null! }, request with { Target = null! },
            request with { SourceLayout = null! },
        })
            Assert.Throws<ArgumentNullException>(() => materializer.Materialize(invalid));
        foreach (var invalid in new[]
        {
            request with { SourceLayout = request.SourceLayout with { NativeImports = default } },
            request with { SourceLayout = request.SourceLayout with { NativeImports = [] } },
            request with { SystemLibraryPaths = default },
            request with { Consumer = request.Consumer with { Target = "wasm64" } },
            request with { Target = request.Target with { Target = "wasm64" } },
            request with { Target = request.Target with { NativeValidation = null } },
        })
            Assert.Throws<InvalidOperationException>(() => materializer.Materialize(invalid));
        Assert.Empty(fixture.Events);
    }

    private static RuntimeNativeMaterializationRequest Request(RuntimeWasmOptimization optimization = RuntimeWasmOptimization.Oz)
    {
        var consumer = new RuntimeMaterializationRequest("/runtime/runtime-pack.json", "/output/layout.json", "/runtime",
            "/tools/wasm-ld", "/tools/wasm-opt", "/tools/node", "/tools/wasm-tools.mjs", "/tools/wasm-tools.wasm",
            "/output/runtime.wasm", "/logs", "/cache", "wasm32", optimization, null, null,
            new("sdk", "compiler", "runtime", "pack", "host-tools", "host-tools-version", "lld", "binaryen", "wasm-tools", "node"))
        { NativeLibraries = [new("mule", "wasm32", "/native/libmule.a")] };
        return new(consumer, RuntimePackTestData.Manifest(), RuntimePackTestData.Target("wasm32"),
            new(3, "wasm32", 65_537) { NativeImports = [new("mule", "run", [], RuntimeNativeValueType.I32)] },
            ["/runtime/wasm32/system-libraries/libc.a"]);
    }

    private static RuntimeNativeMaterializationRequest CallbackRequest(byte[] bytes)
    {
        var support = RuntimePackTestData.CallbackSupport(
            digest: Digest(bytes));
        var consumer = Request().Consumer with
        {
            NativeLibraries = [],
            NativeCallbackObjectPath = "/input/application.callbacks.o",
        };
        return new(
            consumer,
            RuntimePackTestData.Manifest(),
            RuntimePackTestData.Target("wasm32"),
            new RuntimeLayout(4, "wasm32", 65_537)
            {
                NativeCallbackSupport = support,
            },
            ["/runtime/wasm32/system-libraries/libc.a"]);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertRejectedBeforeWork(
        Fixture fixture,
        RuntimeNativeMaterializationRequest request)
    {
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Create().Materialize(request));
        Assert.Null(fixture.NativeKeyRequest);
        Assert.Empty(fixture.Commands);
        Assert.Equal(0, fixture.WorkspaceCreations);
        Assert.Empty(fixture.PublishedPaths);
    }

    public enum CacheDefect
    {
        MissingEvidence, MissingBytes, MissingDigest, InvalidModule, InvalidLayout, InvalidBindings,
        MissingLayout, DefaultBindings, NullBinding, InvalidImports
    }

    private sealed class Fixture : IRuntimeNativeLibraryResolver, IRuntimeNativeProviderSelector, IRuntimeMemoryPlanBuilder,
        IRuntimeNativeArchiveValidationArgumentBuilder, IRuntimeLinkArgumentBuilder, IRuntimeOptimizationArgumentBuilder,
        ILinkedRuntimeModuleReader, ILinkedMemoryLayoutCalculator, ILinkedMemoryLayoutValidator, ILinkerSymbolTraceReader,
        IRuntimeNativeBindingValidator, IRuntimeMaterializationCacheKeyBuilder, IRuntimeMaterializationCacheReader,
        IRuntimeMaterializationCacheWriter, IRuntimeArtifactPublisher, IRuntimeArtifactReader, IRuntimeNativeArchiveSnapshotter,
        ICommandInvoker, IRuntimeNativeLinkWorkspaceFactory
        , IRuntimeLinkExportPlanBuilder, IRuntimeLinkedImportValidator, IRuntimeNativeModuleValidator, IRuntimeNativeValidationProfileValidator
    {
        public RuntimeMemoryPlan Plan { get; } = new("wasm32", 4, 65_536, 16, 65_537, 65_552, 65_536, 2_147_483_648, 65_536);
        public RuntimeLinkedMemoryLayout Expected { get; } = new("wasm32", 65_552, 100_000, 100_000, 165_536, 165_536, 262_144, 2_147_483_648);
        public ImmutableArray<RuntimeNativeBinding> Bindings { get; } = [new(new("mule", "run", [], RuntimeNativeValueType.I32),
            new("mule", "wasm32", "/native/libmule.a", RuntimePackTestData.Digest))];
        public ImmutableArray<RuntimeValidatedNativeBinding> Validated { get; } = [new("run", "/native/libmule.a", RuntimePackTestData.Digest, "mule.o")];
        public byte[] Bytes { get; } = [0, 97, 115, 109];
        public byte[] RuntimeAllowedSymbols { get; init; } =
            System.Text.Encoding.UTF8.GetBytes("runtime_symbol\n");
        public List<string> Events { get; } = [];
        public List<RuntimeCommand> Commands { get; } = [];
        public List<RuntimeLinkRequest> LinkRequests { get; } = [];
        public List<(RuntimeLinkedMemoryLayout Observed, RuntimeLinkedMemoryLayout Expected)> LayoutChecks { get; } = [];
        public List<RuntimeNativeBindingValidationRequest> TraceValidations { get; } = [];
        public List<RuntimeNativeCachedBindingValidationRequest> CachedValidations { get; } = [];
        public RuntimeNativeLibraryResolutionRequest? ResolutionRequest { get; private set; }
        public RuntimeNativeMaterializationCacheKeyRequest? NativeKeyRequest { get; private set; }
        public RuntimeNativeCacheEvidence? WrittenEvidence { get; private set; }
        public byte[]? PublishedBytes { get; private set; }
        public string? PublishedDigest { get; private set; }
        public RuntimeMaterializationCacheRead Cached { get; set; } = new(RuntimeMaterializationCacheOutcome.Miss, null, null);
        public CacheDefect? CacheDefect { get; set; }
        public int FailCommand { get; set; }
        public bool ChangedArchive { get; set; }
        public Exception? CacheWriteFailure { get; set; }
        public InvalidOperationException Failure { get; } = new("tool failure");
        public int ModulesRead { get; private set; }
        public int ArchiveChecks { get; private set; }
        public int WorkspaceCreations { get; private set; }
        public int ImportChecks { get; private set; }
        public int FailImportCheck { get; set; }
        public bool InvalidProfile { get; set; }
        public List<RuntimeNativeModuleValidationRequest> ValidationRequests { get; } = [];
        public List<string> PublishedPaths { get; } = [];
        public Dictionary<string, byte[]> PublishedArtifacts { get; } = [];
        public WorkspaceStub Workspace { get; } = new();

        public IRuntimeNativeModuleMaterializer Create() => Assert.IsAssignableFrom<IRuntimeNativeModuleMaterializer>(
            new RuntimeNativeModuleMaterializer(this, this, this, this,
                this, this, this, this, this, this, this, this, this, this, this, this, this, this, this, this, this, this, this));
        public void Validate(RuntimeNativeValidationProfile profile, string target)
        {
            if (InvalidProfile) throw Failure;
        }
        public RuntimeLinkExportPlan Build(RuntimeLinkExportPlanRequest request) =>
            request.NativeCallbackSupport is null
                ? new([], [new("run", 0)])
                : new([], [new("__netwasm_callback_address_0", 0)]);
        public ImmutableArray<RuntimeNativeLibrary> Resolve(RuntimeNativeLibraryResolutionRequest request)
        { Events.Add("resolve"); ResolutionRequest = request; return [Bindings[0].Provider]; }
        public ImmutableArray<RuntimeNativeBinding> Select(RuntimeNativeProviderSelectionRequest request)
        { Events.Add("select"); return Bindings; }
        public RuntimeMemoryPlan Build(RuntimeMemoryLayoutRequest request) { Events.Add("plan"); return Plan; }
        public ImmutableArray<string> Build(RuntimeNativeArchiveValidationRequest request) => [request.ArchivePath, request.OutputPath];
        public ImmutableArray<string> Build(RuntimeLinkRequest request)
        { LinkRequests.Add(request); return [request.OutputPath]; }
        public ImmutableArray<string> Build(RuntimeOptimizationRequest request) => request.Optimization == RuntimeWasmOptimization.None ? [] : [request.OutputPath];
        public RuntimeMaterializationCacheKey Build(RuntimeMaterializationCacheKeyRequest request) => throw new InvalidOperationException("wrong cache kind");
        public RuntimeMaterializationCacheKey Build(RuntimeNativeMaterializationCacheKeyRequest request)
        {
            NativeKeyRequest = request;
            return new RuntimeMaterializationCacheKeyBuilder().Build(request);
        }
        public RuntimeLinkedModule Read(ReadOnlyMemory<byte> bytes)
        {
            ModulesRead++;
            if (CacheDefect == RuntimeNativeModuleMaterializerTests.CacheDefect.InvalidModule && Commands.Count == 0) throw new InvalidOperationException();
            return new(Expected, [], [], [], []);
        }
        public RuntimeLinkedMemoryLayout Calculate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed)
        { Events.Add("calculate"); return Expected; }
        public void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed) => Events.Add("layout");
        public void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed, RuntimeLinkedMemoryLayout expected)
        {
            if (CacheDefect == RuntimeNativeModuleMaterializerTests.CacheDefect.InvalidLayout && Commands.Count == 0) throw new InvalidOperationException();
            LayoutChecks.Add((observed, expected));
        }
        public ImmutableArray<RuntimeLinkerSymbolEvent> Read(string text) => [];
        public ImmutableArray<RuntimeValidatedNativeBinding> Validate(RuntimeNativeBindingValidationRequest request)
        {
            TraceValidations.Add(request);
            return request.Bindings.Select((binding, index) =>
                Validated[index] with
                {
                    ProviderPath = binding.Provider.Path,
                }).ToImmutableArray();
        }
        public ImmutableArray<RuntimeValidatedNativeBinding> Validate(RuntimeNativeCachedBindingValidationRequest request)
        {
            if (CacheDefect == RuntimeNativeModuleMaterializerTests.CacheDefect.InvalidBindings) throw new InvalidOperationException();
            CachedValidations.Add(request); return Validated;
        }
        public RuntimeMaterializationCacheRead Read(string cacheDirectory, RuntimeMaterializationCacheSlot slot, RuntimeMaterializationCacheKey key)
        { Events.Add("cache-read"); return Cached; }
        public void Write(string cacheDirectory, RuntimeMaterializationCacheSlot slot, RuntimeMaterializationCacheKey key,
            byte[] bytes, string sha256, RuntimeNativeCacheEvidence? nativeEvidence = null)
        {
            Events.Add("cache-write");
            if (CacheWriteFailure is not null) throw CacheWriteFailure;
            WrittenEvidence = nativeEvidence;
        }
        public void PublishIfDifferent(string outputPath, byte[] bytes, string sha256)
        {
            Events.Add("publish");
            PublishedPaths.Add(outputPath);
            PublishedArtifacts[outputPath] = bytes;
            PublishedBytes = bytes;
            PublishedDigest = sha256;
        }
        public void Validate(
            RuntimeNativeValidationProfile profile,
            RuntimeLinkedModule module,
            RuntimeNativeCallbackSupport? callbackSupport = null)
        {
            ImportChecks++;
            if (ImportChecks == FailImportCheck) throw Failure;
            if (CacheDefect == RuntimeNativeModuleMaterializerTests.CacheDefect.InvalidImports && Commands.Count == 0)
                throw new InvalidOperationException();
        }
        public void Validate(RuntimeNativeModuleValidationRequest request)
        {
            ValidationRequests.Add(request);
            Invoke(new(request.NodePath, ["validate", request.Path], request.LogPath));
        }
        byte[] IRuntimeArtifactReader.Read(string path) =>
            path.EndsWith("allowed-undefined-symbols.txt", StringComparison.Ordinal)
                ? RuntimeAllowedSymbols
                : Bytes;
        public ImmutableArray<RuntimeNativeArchiveSnapshot> Snapshot(RuntimeNativeArchiveSnapshotRequest request)
        {
            ArchiveChecks++;
            Assert.Equal(Bindings.Select(binding => binding.Provider), request.Providers);
            Assert.Equal(Workspace.DirectoryPath, request.DirectoryPath);
            if (ChangedArchive) throw new InvalidOperationException("changed archive snapshot");
            return [new(request.Providers[0], Path.Combine(request.DirectoryPath, "native-0.a"))];
        }
        public void Invoke(RuntimeCommand command)
        {
            Commands.Add(command);
            if (Commands.Count == FailCommand) throw Failure;
            if (command.Arguments.Contains("validate")) Events.Add("validate");
        }
        public IRuntimeNativeLinkWorkspace Create(string logDirectory)
        { WorkspaceCreations++; return Workspace; }
    }

    private sealed class WorkspaceStub : IRuntimeNativeLinkWorkspace
    {
        public string DirectoryPath => "/owned/native-link";
        public string LogDirectoryPath => "/owned/native-logs";
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
