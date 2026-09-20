using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Caching.Frontend;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests.Caching.Frontend;

public sealed class FrontendArtifactCacheActorTests
{
    [Fact]
    public void TransportCompositionResolvesOneActionActorsAndFacade()
    {
        var services = new ServiceCollection();
        services.AddNetWasmCompiler();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<FrontendArtifactCachePreparationFactory>(
            provider.GetRequiredService<IFrontendArtifactCachePreparationFactory>());
        Assert.IsType<FrontendArtifactCompilationFactory>(
            provider.GetRequiredService<IFrontendArtifactCompilationFactory>());
        Assert.IsType<FrontendArtifactPreparationCanceler>(
            provider.GetRequiredService<IFrontendArtifactPreparationCanceler>());
        Assert.IsType<FrontendArtifactPublicationFactory>(
            provider.GetRequiredService<IFrontendArtifactPublicationFactory>());
        Assert.IsType<FrontendArtifactPublicationBatchReader>(
            provider.GetRequiredService<IFrontendArtifactPublicationBatchReader>());
        Assert.IsType<FrontendArtifactPublicationBatchAcknowledger>(
            provider.GetRequiredService<IFrontendArtifactPublicationBatchAcknowledger>());
        Assert.IsType<FrontendArtifactPublicationAbandoner>(
            provider.GetRequiredService<IFrontendArtifactPublicationAbandoner>());
        Assert.IsType<FrontendArtifactCacheTransport>(
            provider.GetRequiredService<IFrontendArtifactCacheTransport>());
    }

    [Fact]
    public void ReleasedTransportFacadeOnlyForwardsToItsSevenCapabilities()
    {
        var probe = new ForwardingTransportProbe();
        var transport = new FrontendArtifactCacheTransport(
            probe, probe, probe, probe, probe, probe, probe);
        var options = Options(enabled: true);
        var descriptor = new FrontendArtifactCacheDescriptor(
            "schema", "namespace", "preparation");
        var entries = new List<FrontendArtifactCacheEntry>();
        var publication = new FrontendArtifactCachePublication("token", 0, 0);
        var batch = new FrontendArtifactCacheBatch("token", "batch", [], true);
        probe.Descriptor = descriptor;
        probe.Publication = publication;
        probe.Batch = batch;

        Assert.Same(descriptor, transport.Prepare(options));
        Assert.Same(probe.Scope, transport.BeginCompilation(descriptor, entries));
        transport.CancelPreparation(descriptor);
        Assert.Same(publication, transport.CompleteCompilation(descriptor));
        Assert.Same(batch, transport.ReadBatch(publication));
        transport.AcknowledgeBatch(publication, batch);
        transport.Abandon(publication);

        Assert.Same(options, probe.Options);
        Assert.Same(descriptor, probe.SuppliedDescriptor);
        Assert.Same(entries, probe.Entries);
        Assert.Same(publication, probe.SuppliedPublication);
        Assert.Same(batch, probe.SuppliedBatch);
        Assert.Equal([
            "prepare", "begin", "cancel", "complete", "read",
            "acknowledge", "abandon",
        ], probe.Events);

        var failure = new InvalidOperationException("probe");
        probe.ReadFailure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            transport.ReadBatch(publication)));
    }

    [Fact]
    public void TransportActorsEnforceLifecycleAndCanAbandonPublication()
    {
        var context = Context(null) with { Namespace = new string('a', 64) };
        var memory = new FrontendArtifactMemoryStore();
        var objects = new FrontendArtifactObjectStore();
        var store = new FrontendArtifactTransportStore();
        var transport = CreateTransport(
            new FixedIdentityBuilder(context), memory, objects, store);
        var descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));

        Assert.Throws<InvalidOperationException>(() =>
            transport.Prepare(Options(enabled: true)));
        using var scope = transport.BeginCompilation(descriptor, []);
        Assert.Throws<InvalidOperationException>(() =>
            transport.CompleteCompilation(descriptor with
            {
                Namespace = new string('b', 64),
            }));
        var key = new string('c', 64);
        new FrontendArtifactTransportPublisher(store).Publish(new(context,
            new ConcurrentDictionary<string, ImmutableArray<byte>>
            {
                [key] = [1],
            }));
        var publication = Assert.IsType<FrontendArtifactCachePublication>(
            transport.CompleteCompilation(descriptor));
        Assert.Throws<InvalidOperationException>(() =>
            transport.CompleteCompilation(descriptor));
        Assert.Throws<InvalidOperationException>(() => transport.AcknowledgeBatch(
            publication, new(publication.Token, Guid.NewGuid().ToString("N"), [], true)));

        transport.Abandon(publication);

        Assert.Throws<InvalidOperationException>(() => transport.ReadBatch(publication));
        Assert.Throws<ArgumentNullException>(() => transport.Abandon(null!));
    }

    [Fact]
    public void PublicationFanoutPublishesToActiveTransport()
    {
        var context = Context(null) with { Namespace = new string('a', 64) };
        var memory = new FrontendArtifactMemoryStore();
        var objects = new FrontendArtifactObjectStore();
        var store = new FrontendArtifactTransportStore();
        var transport = CreateTransport(
            new FixedIdentityBuilder(context), memory, objects, store);
        var descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));
        using var scope = transport.BeginCompilation(descriptor, []);
        var publication = new FrontendArtifactPayloadPublication(context,
            new ConcurrentDictionary<string, ImmutableArray<byte>>
            {
                [new string('b', 64)] = [1, 2, 3],
            });
        var fanout = new FrontendArtifactPayloadPublicationFanout(
            new FrontendArtifactPayloadPublisher(),
            new FrontendArtifactTransportPublisher(store));

        fanout.Publish(publication);

        Assert.NotNull(transport.CompleteCompilation(descriptor));
        Assert.Throws<ArgumentNullException>(() => fanout.Publish(null!));
    }

    [Fact]
    public void CacheActorsRemainInactiveOutsideACompilationRequest()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var resolver = new FrontendArtifactCacheRequestResolver(
            new FrontendArtifactCacheState());
        var restorer = new FrontendArtifactRestorer(
            resolver,
            new FrontendArtifactObjectReader(new()),
            new FrontendArtifactPayloadReader(new(), resolver),
            new FrontendArtifactDecoder(),
            FrontendCacheTestFactory.Hydrator(
                new ControlFlowGraphBuilderFactory().Create()));
        var recorder = new FrontendAnalysisRecorder(resolver);
        var structured = new FrontendStructuredMethodRestorer(resolver);
        var stager = new FrontendArtifactStager(
            resolver,
            new FrontendArtifactEligibilityClassifier(),
            new FrontendArtifactSnapshotter(),
            new FrontendArtifactEncoder());

        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        recorder.Record(artifact.Analysis);
        Assert.False(structured.TryRestore(artifact.Analysis.Method, out _));
        stager.Stage(artifact.Analysis.Method, artifact.StructuredMethod);
    }

    [Fact]
    public void AnalyzerReturnsHitWithoutCallingInnerOrRecorder()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var inner = new RecordingAnalyzer(artifact.Analysis);
        var recorder = new RecordingRecorder();
        var analyzer = new CachingReachableMethodAnalyzer(inner,
            new FixedRestorer(artifact), recorder);

        var result = analyzer.Analyze(new(artifact.Analysis.Method));

        Assert.Same(artifact.Analysis, result);
        Assert.Equal(0, inner.CallCount);
        Assert.Null(recorder.Analysis);
    }

    [Fact]
    public void AnalyzerRecordsMiss()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var inner = new RecordingAnalyzer(artifact.Analysis);
        var recorder = new RecordingRecorder();
        var analyzer = new CachingReachableMethodAnalyzer(inner,
            new FixedRestorer(null), recorder);

        Assert.Same(artifact.Analysis, analyzer.Analyze(new(artifact.Analysis.Method)));
        Assert.Equal(1, inner.CallCount);
        Assert.Same(artifact.Analysis, recorder.Analysis);
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(null!));
        Assert.Throws<ArgumentNullException>(() => new CachingReachableMethodAnalyzer(
            null!, new FixedRestorer(null), new RecordingRecorder()));
        Assert.Throws<ArgumentNullException>(() => new CachingReachableMethodAnalyzer(
            inner, null!, new RecordingRecorder()));
        Assert.Throws<ArgumentNullException>(() => new CachingReachableMethodAnalyzer(
            inner, new FixedRestorer(null), null!));
    }

    [Fact]
    public void LowererReturnsHitWithoutCallingInnerOrStager()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var inner = new RecordingLowerer(artifact.StructuredMethod);
        var stager = new RecordingStager();
        var lowerer = new CachingWasmMethodLowerer(inner,
            new FixedStructuredRestorer(artifact.StructuredMethod), stager);

        Assert.Same(artifact.StructuredMethod, lowerer.Lower(artifact.Analysis.Body));
        Assert.Equal(0, inner.CallCount);
        Assert.Null(stager.Structured);
    }

    [Fact]
    public void LowererStagesMiss()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var inner = new RecordingLowerer(artifact.StructuredMethod);
        var stager = new RecordingStager();
        var lowerer = new CachingWasmMethodLowerer(inner,
            new FixedStructuredRestorer(null), stager);

        Assert.Same(artifact.StructuredMethod, lowerer.Lower(artifact.Analysis.Body));
        Assert.Equal(1, inner.CallCount);
        Assert.Same(artifact.StructuredMethod, stager.Structured);
        Assert.Throws<ArgumentNullException>(() => lowerer.Lower(null!));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmMethodLowerer(
            null!, new FixedStructuredRestorer(null), new RecordingStager()));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmMethodLowerer(
            inner, null!, new RecordingStager()));
        Assert.Throws<ArgumentNullException>(() => new CachingWasmMethodLowerer(
            inner, new FixedStructuredRestorer(null), null!));
    }

    [Fact]
    public void PublisherAndReaderRoundTripMemoryPayload()
    {
        var memory = new FrontendArtifactMemoryStore();
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var publisher = new FrontendArtifactPayloadPublisher();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new NullIdentityBuilder(), publisher, new RecordingObjectPublisher());
        using var transaction = factory.Begin(Options(enabled: false));
        var context = Context(directory: null);
        var payload = ImmutableArray.Create<byte>(1, 2, 3);
        memory.Payloads[context.Namespace + "/method"] = payload;
        var reader = new FrontendArtifactPayloadReader(memory, resolver);

        Assert.True(reader.TryRead(new(context, "method"), out var restored));
        Assert.Equal(payload, restored);
        Assert.Equal(1, state.Active!.MemoryHits);
    }

    [Fact]
    public void TransportImportsValidatedPayloadsAndExportsOnlyCommittedPublication()
    {
        var cacheNamespace = new string('a', 64);
        var context = Context(null) with { Namespace = cacheNamespace };
        var memory = new FrontendArtifactMemoryStore();
        var objects = new FrontendArtifactObjectStore();
        var store = new FrontendArtifactTransportStore();
        var identities = new CountingIdentityBuilder(context);
        var transport = CreateTransport(identities, memory, objects, store);
        var descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));
        var key = new string('b', 64);
        FrontendArtifactCacheBatch batch;
        using (transport.BeginCompilation(descriptor, []))
        {
            new FrontendArtifactTransportPublisher(store).Publish(new(context,
                new ConcurrentDictionary<string, ImmutableArray<byte>>
                {
                    [key] = [1, 2, 3],
                }));
            var publication = Assert.IsType<FrontendArtifactCachePublication>(
                transport.CompleteCompilation(descriptor));
            batch = transport.ReadBatch(publication);
            var replay = transport.ReadBatch(publication);
            Assert.Equal(batch.BatchToken, replay.BatchToken);
            replay.Entries[0].Payload[0] = 9;
            Assert.Equal(1, transport.ReadBatch(publication).Entries[0].Payload[0]);
            transport.AcknowledgeBatch(publication, batch);
            Assert.Throws<InvalidOperationException>(() => transport.ReadBatch(publication));
        }
        descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));
        using (transport.BeginCompilation(descriptor,
        [
            batch.Entries[0] with { Key = new string('c', 64) },
        ]))
            Assert.Empty(memory.Payloads);
        descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));
        using (transport.BeginCompilation(descriptor, batch.Entries))
        {

            var state = new FrontendArtifactCacheState();
            var resolver = new FrontendArtifactCacheRequestResolver(state);
            var factory = new FrontendArtifactCacheRequestFactory(state, store, memory,
                identities, new RecordingPublisher(),
                new RecordingObjectPublisher());
            var buildsBeforeRequest = identities.BuildCount;
            using var request = factory.Begin(Options(enabled: true));
            Assert.Equal(buildsBeforeRequest, identities.BuildCount);
            Assert.True(new FrontendArtifactPayloadReader(memory, resolver).TryRead(
                new(context, key), out var imported));
            Assert.Equal(new byte[] { 1, 2, 3 }, imported);
            Assert.Equal(1, state.Active!.DiskHits);
        }
    }

    [Fact]
    public void TransportRejectsInvalidBatchesAtomically()
    {
        var context = Context(null) with { Namespace = new string('a', 64) };
        var memory = new FrontendArtifactMemoryStore();
        var objects = new FrontendArtifactObjectStore();
        var store = new FrontendArtifactTransportStore();
        var transport = CreateTransport(
            new FixedIdentityBuilder(context), memory, objects, store);
        var descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));

        using (transport.BeginCompilation(descriptor,
        [
            new(new string('b', 64), [1], new byte[SHA256.HashSizeInBytes]),
            new(new string('c', 64), [2], new byte[SHA256.HashSizeInBytes]),
        ])) { }
        Assert.Empty(memory.Payloads);
        Assert.Throws<ArgumentException>(() => transport.BeginCompilation(
            descriptor with { Schema = "old" }, []));
        descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));
        transport.CancelPreparation(descriptor);
        Assert.Throws<InvalidOperationException>(() =>
            transport.CancelPreparation(descriptor));
        Assert.Throws<InvalidOperationException>(() =>
            transport.BeginCompilation(descriptor, []));
        Assert.NotNull(transport.Prepare(Options(enabled: true)));
    }

    [Fact]
    public void TransportPublishesBoundedAcknowledgedBatchesOnlyWhileActive()
    {
        var context = Context(null) with { Namespace = new string('a', 64) };
        var memory = new FrontendArtifactMemoryStore();
        var store = new FrontendArtifactTransportStore();
        var publisher = new FrontendArtifactTransportPublisher(store);
        var entries = new ConcurrentDictionary<string, ImmutableArray<byte>>(
            Enumerable.Range(0, 257).ToDictionary(index => index.ToString("x64",
                    System.Globalization.CultureInfo.InvariantCulture),
                _ => ImmutableArray.Create<byte>(1), StringComparer.Ordinal),
            StringComparer.Ordinal);
        publisher.Publish(new(context, entries));
        Assert.Null(store.Publication);
        var transport = CreateTransport(
            new FixedIdentityBuilder(context), memory, new(), store);
        var descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));
        using var scope = transport.BeginCompilation(descriptor, []);
        publisher.Publish(new(context, entries));
        var publication = Assert.IsType<FrontendArtifactCachePublication>(
            transport.CompleteCompilation(descriptor));

        var first = transport.ReadBatch(publication);
        Assert.Equal(256, first.Entries.Count);
        Assert.False(first.IsFinal);
        transport.AcknowledgeBatch(publication, first);
        var second = transport.ReadBatch(publication);
        Assert.Single(second.Entries);
        Assert.True(second.IsFinal);
        transport.AcknowledgeBatch(publication, second);
        Assert.Throws<InvalidOperationException>(() => transport.ReadBatch(publication));
    }

    [Fact]
    public void RequestPublishesOnlyAfterCommit()
    {
        var state = new FrontendArtifactCacheState();
        var publisher = new RecordingPublisher();
        var context = Context(null);
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FixedIdentityBuilder(context), publisher, new RecordingObjectPublisher());
        using (factory.Begin(Options(enabled: true))) { }
        Assert.Null(publisher.Publication);
        using (var request = factory.Begin(Options(enabled: true))) request.Commit();
        Assert.NotNull(publisher.Publication);
    }

    [Fact]
    public void RequestAlwaysReleasesItsActiveStateWhenPublicationFails()
    {
        var state = new FrontendArtifactCacheState();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FixedIdentityBuilder(Context(null)), new ThrowingPublisher(),
            new RecordingObjectPublisher());
        var request = factory.Begin(Options(enabled: true));
        request.Commit();

        Assert.Throws<IOException>(() => request.Dispose());
        Assert.Null(state.Active);
        using var recovery = factory.Begin(Options(enabled: false));
        Assert.Same(state.Active, recovery);
    }

    [Fact]
    public void OperationsStagePublishAndRestoreCompletedFrontend()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var dependency = artifact.Analysis.Method.Definition.Key.Assembly.Name;
        var context = new FrontendArtifactCacheContext("namespace", new("Entry"),
            ImmutableDictionary<string, string>.Empty.Add(dependency, "content"), null);
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var memory = new FrontendArtifactMemoryStore();
        var publisher = new FrontendArtifactPayloadPublisher();
        var objectStore = new FrontendArtifactObjectStore();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FixedIdentityBuilder(context), publisher,
            new FrontendArtifactObjectPublisher(objectStore));
        var recorder = new FrontendAnalysisRecorder(resolver);
        var stager = new FrontendArtifactStager(resolver,
            new FrontendArtifactEligibilityClassifier(),
            new FrontendArtifactSnapshotter(), new FrontendArtifactEncoder());

        using (var request = factory.Begin(Options(enabled: true)))
        {
            recorder.Record(artifact.Analysis);
            stager.Stage(artifact.Analysis.Method, artifact.StructuredMethod);
            request.Commit();
        }

        using var replay = factory.Begin(Options(enabled: true));
        var restorer = new FrontendArtifactRestorer(resolver,
            new FrontendArtifactObjectReader(objectStore),
            new FrontendArtifactPayloadReader(memory, resolver),
            new FrontendArtifactDecoder(),
            FrontendCacheTestFactory.Hydrator(new ControlFlowGraphBuilderFactory().Create()));
        Assert.True(restorer.TryRestore(artifact.Analysis.Method, out var restored));
        Assert.Equal(artifact.Analysis.Method.CanonicalName,
            restored.Analysis.Method.CanonicalName);
        var lowering = new FrontendStructuredMethodRestorer(resolver);
        Assert.True(lowering.TryRestore(artifact.Analysis.Method, out var structured));
        Assert.Equal(artifact.StructuredMethod.Blocks.Count, structured.Blocks.Count);
        Assert.False(lowering.TryRestore(artifact.Analysis.Method, out _));
        var metrics = new FrontendArtifactCacheMetricsReader(resolver).Read();
        Assert.Equal((1, 1, 0, 1, 0, 0),
            (metrics.Lookups, metrics.Hits, metrics.Misses, metrics.MemoryHits,
                metrics.DiskHits, metrics.StagedArtifacts));
    }

    [Fact]
    public void RestoreFallsBackToColdPathWhenStructuralBudgetIsExhausted()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var dependency = artifact.Analysis.Method.Definition.Key.Assembly.Name;
        var context = new FrontendArtifactCacheContext("namespace", new("Entry"),
            ImmutableDictionary<string, string>.Empty.Add(dependency, "content"), null);
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var memory = new FrontendArtifactMemoryStore();
        var objects = new FrontendArtifactObjectStore();
        var transport = new FrontendArtifactTransportStore();
        var active = new ActiveFrontendArtifactCache(context);
        Assert.True(active.Budget.TryReserve(0, 0,
            FrontendArtifactCachePolicy.MaximumStructuralWorkingSetBytes));
        transport.Active.Value = active;
        var key = context.MethodKey(artifact.Analysis.Method);
        memory.Payloads[context.Namespace + "/" + key] =
            new FrontendArtifactEncoder().Encode(
                new FrontendArtifactSnapshotter().Capture(artifact));
        var factory = new FrontendArtifactCacheRequestFactory(state, transport, memory,
            new FixedIdentityBuilder(context), new RecordingPublisher(),
            new FrontendArtifactObjectPublisher(objects));
        using var request = factory.Begin(Options(enabled: true));
        var restorer = new FrontendArtifactRestorer(resolver,
            new FrontendArtifactObjectReader(objects),
            new FrontendArtifactPayloadReader(memory, resolver),
            new FrontendArtifactDecoder(),
            FrontendCacheTestFactory.Hydrator(new ControlFlowGraphBuilderFactory().Create()));

        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        Assert.Equal(1, state.Active!.Misses);
        Assert.Empty(state.Active.RestoredStructuralKeys);
    }

    [Fact]
    public void RequestFactoryClearsOtherNamespaceAndRejectsOversizeSameNamespace()
    {
        var context = Context(null) with { Namespace = new string('a', 64) };
        var memory = new FrontendArtifactMemoryStore
        {
            Namespace = new string('b', 64),
            EntryCount = 1,
            TotalBytes = 1,
        };
        memory.Payloads["old/key"] = [1];
        memory.LoadedNamespaces.TryAdd(memory.Namespace, 0);
        var state = new FrontendArtifactCacheState();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FrontendArtifactTransportStore(), memory,
            new FixedIdentityBuilder(context), new RecordingPublisher(),
            new RecordingObjectPublisher());

        using (factory.Begin(Options(enabled: true)))
        {
            Assert.Empty(memory.Payloads);
            Assert.Empty(memory.LoadedNamespaces);
            Assert.Null(memory.Namespace);
            Assert.Equal(0, memory.EntryCount);
            Assert.Equal(0, memory.TotalBytes);
        }

        memory.Namespace = context.Namespace;
        memory.EntryCount = FrontendArtifactCachePolicy.MaximumArtifacts + 1;
        Assert.Throws<InvalidOperationException>(() =>
            factory.Begin(Options(enabled: true)));
        Assert.Null(state.Active);
    }

    [Fact]
    public void DisabledOperationsMissWithoutStaging()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var memory = new FrontendArtifactMemoryStore();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new NullIdentityBuilder(), new FrontendArtifactPayloadPublisher(),
            new RecordingObjectPublisher());
        using var request = factory.Begin(Options(enabled: false));
        var recorder = new FrontendAnalysisRecorder(resolver);
        recorder.Record(artifact.Analysis);
        var stager = new FrontendArtifactStager(resolver,
            new FrontendArtifactEligibilityClassifier(),
            new FrontendArtifactSnapshotter(), new FrontendArtifactEncoder());
        stager.Stage(artifact.Analysis.Method, artifact.StructuredMethod);
        var restorer = new FrontendArtifactRestorer(resolver,
            new FrontendArtifactObjectReader(new()),
            new FrontendArtifactPayloadReader(memory, resolver),
            new FrontendArtifactDecoder(),
            FrontendCacheTestFactory.Hydrator(new ControlFlowGraphBuilderFactory().Create()));
        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        Assert.Equal(1, state.Active!.Misses);
        Assert.Empty(state.Active.Staged);
    }

    [Fact]
    public void IdentityIncludesCompilerOptionsAndRejectsUnreadableInput()
    {
        var builder = new FrontendArtifactCacheIdentityBuilder(
            new EntryAssemblyBindingFingerprinter(),
            new ManagedAssemblyImageReader(),
            new CompilationInputHasher(),
            new FrontendArtifactCompilerIdentity());
        var intermediate = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var wit = Path.GetTempFileName();
        File.WriteAllText(wit, "fixture");
        var options = new CompilerOptions(
            typeof(NetWasmCompiler).Assembly.Location,
            [typeof(AssemblyIdentity).Assembly.Location],
            "Program", "Main", [new("run", "Program", "Run")],
            WitPath: wit,
            WitWorld: "fixture:world/world",
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("alias", "value"),
            IntermediateOutputPath: intermediate);

        var context = builder.Build(options);

        Assert.NotNull(context);
        Assert.Equal("NetWasm.Compiler", context.EntryAssembly.Name);
        Assert.StartsWith(Path.GetFullPath(intermediate), context.Directory, StringComparison.Ordinal);
        Assert.Equal(64, context.Namespace.Length);
        var missingWit = builder.Build(options with
        {
            IntermediateOutputPath = null,
            ReferenceAssemblyAliases = null,
            WitPath = intermediate + ".missing-wit",
        });
        Assert.Null(missingWit);
        var memoryOnly = builder.Build(options with
        {
            IntermediateOutputPath = null,
            ReferenceAssemblyAliases = null,
            WitPath = null,
        });
        Assert.NotNull(memoryOnly);
        Assert.Null(memoryOnly.Directory);
        var wasm64 = builder.Build(options with
        {
            IntermediateOutputPath = null,
            Target = WasmTarget.Wasm64,
        });
        Assert.NotNull(wasm64);
        Assert.NotEqual(memoryOnly.Namespace, wasm64.Namespace);
        Assert.Null(builder.Build(options with { EntryAssemblyPath = intermediate + ".missing" }));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<ArgumentNullException>(() => new FrontendArtifactCacheIdentityBuilder(
            null!, new ManagedAssemblyImageReader(), new CompilationInputHasher(),
            new FrontendArtifactCompilerIdentity()));
        Assert.Throws<ArgumentNullException>(() => new FrontendArtifactCacheIdentityBuilder(
            new EntryAssemblyBindingFingerprinter(), null!, new CompilationInputHasher(),
            new FrontendArtifactCompilerIdentity()));
        Assert.Throws<ArgumentNullException>(() => new FrontendArtifactCacheIdentityBuilder(
            new EntryAssemblyBindingFingerprinter(), new ManagedAssemblyImageReader(), null!,
            new FrontendArtifactCompilerIdentity()));
        File.Delete(wit);
    }

    [Fact]
    public void CompilerComponentIdentityInvalidatesBrowserAndObjNamespaces()
    {
        var intermediate = Path.Combine(Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        var options = new CompilerOptions(
            typeof(NetWasmCompiler).Assembly.Location,
            [typeof(AssemblyIdentity).Assembly.Location],
            "Program", "Main", [],
            IntermediateOutputPath: intermediate);
        FrontendArtifactCacheContext? Build(string identity) =>
            new FrontendArtifactCacheIdentityBuilder(
                new EntryAssemblyBindingFingerprinter(),
                new ManagedAssemblyImageReader(),
                new CompilationInputHasher(),
                new FixedCompilerIdentity(identity)).Build(options);

        var first = Build("compiler-components-a");
        var repeat = Build("compiler-components-a");
        var upgraded = Build("compiler-components-b");

        Assert.NotNull(first);
        Assert.NotNull(repeat);
        Assert.NotNull(upgraded);
        Assert.Equal(first.Namespace, repeat.Namespace);
        Assert.Equal(first.Directory, repeat.Directory);
        Assert.NotEqual(first.Namespace, upgraded.Namespace);
        Assert.NotEqual(first.Directory, upgraded.Directory);
    }

    [Fact]
    public void UpgradedCompilerNamespaceRejectsPriorBrowserEntries()
    {
        var oldNamespace = new string('a', 64);
        var upgradedNamespace = new string('b', 64);
        var key = new string('c', 64);
        byte[] payload = [1, 2, 3];
        var stale = new FrontendArtifactCacheEntry(key, payload,
            FrontendArtifactCacheTransportProtocol.Checksum(
                FrontendArtifactCacheIdentityBuilder.Schema, oldNamespace, key, payload));
        var context = Context(null) with { Namespace = upgradedNamespace };
        var memory = new FrontendArtifactMemoryStore();
        var store = new FrontendArtifactTransportStore();
        var transport = CreateTransport(
            new FixedIdentityBuilder(context), memory, new(), store);
        var descriptor = Assert.IsType<FrontendArtifactCacheDescriptor>(
            transport.Prepare(Options(enabled: true)));

        using (transport.BeginCompilation(descriptor, [stale]))
            Assert.Empty(memory.Payloads);
    }

    [Fact]
    public void BindingFingerprintIgnoresMvidAndMethodBodyRvasButRetainsMetadata()
    {
        var original = File.ReadAllBytes(typeof(NetWasmCompiler).Assembly.Location);
        var changedNoise = (byte[])original.Clone();
        using (var stream = new MemoryStream(changedNoise, writable: true))
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            var metadata = pe.GetMetadataReader();
            Assert.True(pe.PEHeaders.TryGetDirectoryOffset(
                pe.PEHeaders.CorHeader!.MetadataDirectory, out var metadataOffset));
            var guid = metadata.GetHeapMetadataOffset(HeapIndex.Guid) +
                MetadataTokens.GetHeapOffset(metadata.GetModuleDefinition().Mvid) - 1;
            changedNoise.AsSpan(metadataOffset + guid, 16).Fill(0x5a);
            var table = metadata.GetTableMetadataOffset(TableIndex.MethodDef);
            var rowSize = metadata.GetTableRowSize(TableIndex.MethodDef);
            for (var row = 0; row < metadata.MethodDefinitions.Count; row++)
            {
                var rva = changedNoise.AsSpan(
                    metadataOffset + table + row * rowSize, sizeof(int));
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(rva) != 0)
                    rva.Fill(0x33);
            }
        }
        var changedMetadata = (byte[])original.Clone();
        using (var stream = new MemoryStream(changedMetadata, writable: true))
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            Assert.True(pe.PEHeaders.TryGetDirectoryOffset(
                pe.PEHeaders.CorHeader!.MetadataDirectory, out var metadataOffset));
            var strings = pe.GetMetadataReader().GetHeapMetadataOffset(HeapIndex.String);
            changedMetadata[metadataOffset + strings + 1] ^= 1;
        }
        var fingerprinter = new EntryAssemblyBindingFingerprinter();

        var expected = fingerprinter.Fingerprint([.. original]);

        Assert.Equal(expected, fingerprinter.Fingerprint([.. changedNoise]));
        Assert.NotEqual(expected, fingerprinter.Fingerprint([.. changedMetadata]));
        Assert.Throws<ArgumentException>(() => fingerprinter.Fingerprint([]));
    }

    [Fact]
    public void BindingFingerprintIgnoresSameAndDifferentLengthUserStrings()
    {
        var fingerprinter = new EntryAssemblyBindingFingerprinter();
        var original = CompileBindingFixture("alpha");
        var sameLength = CompileBindingFixture("omega");
        var differentLength = CompileBindingFixture("a much longer body-only literal");

        var expected = fingerprinter.Fingerprint(original);

        Assert.Equal(expected, fingerprinter.Fingerprint(sameLength));
        Assert.Equal(expected, fingerprinter.Fingerprint(differentLength));
    }

    [Theory]
    [InlineData("public static class Program { public static string Renamed() => \"alpha\"; }")]
    [InlineData("public static class Program { public static object Main() => \"alpha\"; }")]
    [InlineData("public static class Program { public static string Main() => \"alpha\"; } public sealed class Added { }")]
    [InlineData("public interface IMarker { } public static class Program { public static string Main() => \"alpha\"; }")]
    [InlineData("public static class Program { [System.Obsolete] public static string Main() => \"alpha\"; }")]
    [InlineData("public static class Program<T> where T : class { public static string Main() => \"alpha\"; }")]
    public void BindingFingerprintRetainsStructuralMetadata(string changedSource)
    {
        var fingerprinter = new EntryAssemblyBindingFingerprinter();
        var original = CompileBindingFixture("alpha");

        Assert.NotEqual(fingerprinter.Fingerprint(original),
            fingerprinter.Fingerprint(CompileBindingFixtureSource(changedSource)));
    }

    [Fact]
    public void BindingFingerprintRetainsMethodBodyPresence()
    {
        var original = CompileBindingFixture("alpha").ToArray();
        var bodyRemoved = (byte[])original.Clone();
        using (var stream = new MemoryStream(bodyRemoved, writable: true))
        using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
        {
            Assert.True(pe.PEHeaders.TryGetDirectoryOffset(
                pe.PEHeaders.CorHeader!.MetadataDirectory, out var metadataOffset));
            var metadata = pe.GetMetadataReader();
            var tableOffset = metadata.GetTableMetadataOffset(TableIndex.MethodDef);
            var rowSize = metadata.GetTableRowSize(TableIndex.MethodDef);
            var row = MetadataTokens.GetRowNumber(metadata.MethodDefinitions.Last()) - 1;
            bodyRemoved.AsSpan(metadataOffset + tableOffset + row * rowSize, sizeof(int)).Clear();
        }
        var fingerprinter = new EntryAssemblyBindingFingerprinter();

        Assert.NotEqual(fingerprinter.Fingerprint([.. original]),
            fingerprinter.Fingerprint([.. bodyRemoved]));
    }

    [Fact]
    public void MetadataRootReaderRejectsTruncationAndInvalidStreamBounds()
    {
        Assert.Throws<BadImageFormatException>(() => new MetadataRootReader([]));

        var oversizedVersion = new byte[16];
        BitConverter.GetBytes(uint.MaxValue).CopyTo(oversizedVersion, 12);
        Assert.Throws<BadImageFormatException>(() => new MetadataRootReader(oversizedVersion));

        var unalignableVersion = new byte[17];
        BitConverter.GetBytes(1u).CopyTo(unalignableVersion, 12);
        Assert.Throws<BadImageFormatException>(() => new MetadataRootReader(unalignableVersion));

        var unterminatedName = new byte[31];
        BitConverter.GetBytes((ushort)1).CopyTo(unterminatedName, 18);
        unterminatedName.AsSpan(28).Fill((byte)'x');
        Assert.Throws<BadImageFormatException>(() => new MetadataRootReader(unterminatedName));

        var invalidStream = new byte[32];
        BitConverter.GetBytes((ushort)1).CopyTo(invalidStream, 18);
        BitConverter.GetBytes(33u).CopyTo(invalidStream, 20);
        invalidStream[28] = (byte)'#';
        invalidStream[29] = (byte)'~';
        Assert.Throws<BadImageFormatException>(() => new MetadataRootReader(invalidStream));
    }

    [Fact]
    public void DiskBundleRoundTripsInANewMemoryStoreAndRejectsCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var context = Context(directory);
            var firstState = new FrontendArtifactCacheState();
            var firstResolver = new FrontendArtifactCacheRequestResolver(firstState);
            var publisher = new FrontendArtifactPayloadPublisher();
            var factory = new FrontendArtifactCacheRequestFactory(firstState,
                new FixedIdentityBuilder(context), publisher, new RecordingObjectPublisher());
            using (var request = factory.Begin(Options(enabled: true)))
            {
                firstState.Active!.Staged["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"] = [1, 2, 3];
                request.Commit();
            }

            var secondState = new FrontendArtifactCacheState();
            var secondResolver = new FrontendArtifactCacheRequestResolver(secondState);
            var secondFactory = new FrontendArtifactCacheRequestFactory(secondState,
                new FixedIdentityBuilder(context), new RecordingPublisher(),
                new RecordingObjectPublisher());
            using var secondRequest = secondFactory.Begin(Options(enabled: true));
            Assert.False(new FrontendArtifactPayloadReader(
                new FrontendArtifactMemoryStore(), secondResolver).TryRead(new(context,
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), out _));
            var reader = new FrontendArtifactPayloadReader(
                new FrontendArtifactMemoryStore(), secondResolver);
            Assert.True(reader.TryRead(new(context,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), out var payload));
            Assert.Equal(new byte[] { 1, 2, 3 }, payload);
            Assert.Equal(1, secondState.Active!.DiskHits);
            Assert.True(reader.TryRead(new(context,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), out _));
            Assert.False(reader.TryRead(new(context,
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), out _));

            File.WriteAllBytes(Path.Combine(directory, "artifacts.bin"), [0]);
            var corruptState = new FrontendArtifactCacheState();
            var corruptResolver = new FrontendArtifactCacheRequestResolver(corruptState);
            var corruptFactory = new FrontendArtifactCacheRequestFactory(corruptState,
                new FixedIdentityBuilder(context), new RecordingPublisher(),
                new RecordingObjectPublisher());
            using var corruptRequest = corruptFactory.Begin(Options(enabled: true));
            Assert.False(new FrontendArtifactPayloadReader(
                new FrontendArtifactMemoryStore(), corruptResolver).TryRead(new(context,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), out _));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void DiskBundleRejectsMalformedEnvelope(int corruption)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var context = Context(directory);
            var key = new string('a', 64);
            new FrontendArtifactPayloadPublisher().Publish(new(context,
                new ConcurrentDictionary<string, ImmutableArray<byte>> { [key] = [1] }));
            var path = Path.Combine(directory, "artifacts.bin");
            var bytes = File.ReadAllBytes(path);
            bytes = corruption switch
            {
                0 => bytes[..4],
                1 => Mutate(bytes, 0, 0),
                2 => ReplaceInt32(bytes, 4, -1),
                3 => ReplaceInt32(bytes, 4, 100_001),
                4 => Mutate(bytes, 8, 1),
                5 => ReplaceInt32(bytes, 73, -1),
                6 => ReplaceInt32(bytes, 73, 4 * 1024 * 1024 + 1),
                7 => Mutate(bytes, 77, (byte)(bytes[77] ^ 1)),
                8 => DuplicateBundleEntry(bytes),
                9 => [.. bytes, 0],
                10 => bytes,
                11 => ReplaceInt32(bytes, 73, 100),
                _ => throw new InvalidOperationException(),
            };
            File.WriteAllBytes(path, bytes);
            if (corruption == 10)
            {
                using var oversized = new FileStream(path, FileMode.Open, FileAccess.Write);
                oversized.SetLength(72L * 1024 * 1024 + 1);
            }
            var state = new FrontendArtifactCacheState();
            var resolver = new FrontendArtifactCacheRequestResolver(state);
            var factory = new FrontendArtifactCacheRequestFactory(state,
                new FixedIdentityBuilder(context), new RecordingPublisher(),
                new RecordingObjectPublisher());
            using var request = factory.Begin(Options(enabled: true));

            Assert.False(new FrontendArtifactPayloadReader(
                new FrontendArtifactMemoryStore(), resolver).TryRead(new(context, key), out _));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RequestAndResolverEnforceOneActiveTransaction()
    {
        var state = new FrontendArtifactCacheState();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new NullIdentityBuilder(), new RecordingPublisher(),
            new RecordingObjectPublisher());
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        Assert.Null(resolver.Resolve());
        Assert.Equal(
            new FrontendArtifactCacheMetrics(0, 0, 0, 0, 0, 0, 0),
            new FrontendArtifactCacheMetricsReader(resolver).Read());
        Assert.False(new FrontendArtifactPayloadReader(
            new FrontendArtifactMemoryStore(), resolver).TryRead(
                new(Context(null), new string('a', 64)), out var inactivePayload));
        Assert.True(inactivePayload.IsDefault);
        Assert.Throws<ArgumentNullException>(() => factory.Begin(null!));
        var request = factory.Begin(Options(enabled: false));
        Assert.Same(state.Active, resolver.Resolve());
        Assert.Throws<InvalidOperationException>(() => factory.Begin(Options(enabled: false)));
        new FrontendArtifactCacheRequest(state, null,
            new FrontendArtifactWorkingSetBudget(), new RecordingPublisher(),
            new RecordingObjectPublisher()).Dispose();
        request.Dispose();
        request.Dispose();
        Assert.Throws<ObjectDisposedException>(() => request.Commit());
    }

    [Fact]
    public async Task RequestStateIsIsolatedAcrossConcurrentLogicalFlows()
    {
        var state = new FrontendArtifactCacheState();
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new NullIdentityBuilder(), new RecordingPublisher(),
            new RecordingObjectPublisher());
        using var ready = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();

        async Task<FrontendArtifactCacheRequest> BeginAsync()
        {
            return await Task.Run(() =>
            {
                var request = Assert.IsType<FrontendArtifactCacheRequest>(
                    factory.Begin(Options(enabled: false)));
                Assert.Same(request, state.Active);
                ready.Signal();
                release.Wait();
                request.Commit();
                request.Dispose();
                Assert.Null(state.Active);
                return request;
            });
        }

        var first = BeginAsync();
        var second = BeginAsync();
        Assert.True(SpinWait.SpinUntil(() => ready.IsSet, TimeSpan.FromSeconds(5)));
        release.Set();
        var requests = await Task.WhenAll(first, second);

        Assert.NotSame(requests[0], requests[1]);
        Assert.Null(state.Active);
    }

    [Fact]
    public void PayloadPublicationFailureDoesNotEscape()
    {
        var file = Path.GetTempFileName();
        try
        {
            var context = Context(Path.Combine(file, "child"));
            var payloads = new ConcurrentDictionary<string, ImmutableArray<byte>>
            {
                [new string('a', 64)] = [1],
            };
            new FrontendArtifactPayloadPublisher().Publish(new(context, payloads));
            Assert.Throws<ArgumentNullException>(() =>
                new FrontendArtifactPayloadPublisher().Publish(null!));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ObjectStoreIsBoundedToTheLatestNamespace()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var store = new FrontendArtifactObjectStore();
        var publisher = new FrontendArtifactObjectPublisher(store);
        var first = Context(null);
        var second = first with { Namespace = "second" };
        var entries = new ConcurrentDictionary<string, FrontendArtifact>
        {
            ["method"] = artifact,
        };
        var structuralBytes = new ConcurrentDictionary<string, long>
        {
            ["method"] = FrontendArtifactCachePolicy.MaximumStructuralWorkingSetBytes,
        };
        publisher.Publish(new(first, entries, structuralBytes));
        publisher.Publish(new(first, entries, structuralBytes));
        var reader = new FrontendArtifactObjectReader(store);
        Assert.True(reader.TryRead(new(first, "method"), out _));
        publisher.Publish(new(first,
            new ConcurrentDictionary<string, FrontendArtifact>
            {
                ["other"] = artifact,
            },
            new ConcurrentDictionary<string, long>
            {
                ["other"] = 1,
            }));
        Assert.False(reader.TryRead(new(first, "other"), out _));
        Assert.Equal(FrontendArtifactCachePolicy.MaximumStructuralWorkingSetBytes,
            store.TotalStructuralBytes);

        publisher.Publish(new(second, entries, structuralBytes));

        Assert.False(reader.TryRead(new(first, "method"), out _));
        Assert.True(reader.TryRead(new(second, "method"), out _));
        Assert.Throws<ArgumentNullException>(() => reader.TryRead(null!, out _));
        Assert.Throws<ArgumentNullException>(() => publisher.Publish(null!));
    }

    [Fact]
    public void EligibleCacheMissAndCorruptPayloadFallBack()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var dependency = artifact.Analysis.Method.Definition.Key.Assembly.Name;
        var context = new FrontendArtifactCacheContext("namespace", new("Entry"),
            ImmutableDictionary<string, string>.Empty.Add(dependency, "content"), null);
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FixedIdentityBuilder(context), new RecordingPublisher(),
            new RecordingObjectPublisher());
        using var request = factory.Begin(Options(enabled: true));
        var memory = new FrontendArtifactMemoryStore();
        var restorer = new FrontendArtifactRestorer(resolver,
            new FrontendArtifactObjectReader(new()),
            new FrontendArtifactPayloadReader(memory, resolver),
            new FrontendArtifactDecoder(),
            FrontendCacheTestFactory.Hydrator(new ControlFlowGraphBuilderFactory().Create()));
        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        var key = context.MethodKey(artifact.Analysis.Method);
        memory.Payloads[context.Namespace + "/" + key] = [1, 2, 3];
        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        var snapshot = new FrontendArtifactSnapshotter().Capture(artifact);
        memory.Payloads[context.Namespace + "/" + key] = new FrontendArtifactEncoder().Encode(
            snapshot with
            {
                StructuredMethod = snapshot.StructuredMethod with
                {
                    EntryBlock = new(99),
                },
            });
        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        memory.Payloads[context.Namespace + "/" + key] =
            new FrontendArtifactEncoder().Encode(snapshot);
        Assert.True(restorer.TryRestore(artifact.Analysis.Method, out _));
        var wrongMethod = snapshot.Analysis.Method with
        {
            Definition = snapshot.Analysis.Method.Definition with
            {
                Key = new(snapshot.Analysis.Method.Definition.Key.Assembly, 0x06000002),
                Name = "Other",
            },
        };
        memory.Payloads[context.Namespace + "/" + key] =
            new FrontendArtifactEncoder().Encode(snapshot with
            {
                Analysis = snapshot.Analysis with { Method = wrongMethod },
            });
        Assert.False(restorer.TryRestore(artifact.Analysis.Method, out _));
        Assert.Equal(4, state.Active!.Misses);
    }

    [Fact]
    public void StagingEnforcesThePerRequestBudget()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var dependency = artifact.Analysis.Method.Definition.Key.Assembly.Name;
        var context = new FrontendArtifactCacheContext("namespace", new("Entry"),
            ImmutableDictionary<string, string>.Empty.Add(dependency, "content"), null);
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FixedIdentityBuilder(context), new RecordingPublisher(),
            new RecordingObjectPublisher());
        using var request = factory.Begin(Options(enabled: true));
        Assert.True(state.Active!.Budget.TryReserve(0,
            FrontendArtifactCachePolicy.MaximumEncodedWorkingSetBytes, 0));
        state.Active.Analyses[artifact.Analysis.Method.CanonicalName] = artifact.Analysis;
        new FrontendArtifactStager(resolver, new FrontendArtifactEligibilityClassifier(),
            new FrontendArtifactSnapshotter(), new FrontendArtifactEncoder()).Stage(
                artifact.Analysis.Method, artifact.StructuredMethod);
        Assert.Empty(state.Active.Staged);
        Assert.Empty(state.Active.StagedObjects);
        Assert.Equal(0, state.Active.StagedArtifacts);
        Assert.Equal(0, state.Active.StagedBytes);
        request.Dispose();
        using var secondRequest = factory.Begin(Options(enabled: true));
        state.Active.Analyses[artifact.Analysis.Method.CanonicalName] = artifact.Analysis;
        new FrontendArtifactStager(resolver, new FrontendArtifactEligibilityClassifier(),
            new FrontendArtifactSnapshotter(), new OversizeEncoder()).Stage(
                artifact.Analysis.Method, artifact.StructuredMethod);
        Assert.Empty(state.Active.Staged);
        Assert.Empty(state.Active.StagedObjects);
        Assert.Equal(0, state.Active.StagedArtifacts);
        state.Active.Analyses[artifact.Analysis.Method.CanonicalName] = artifact.Analysis;
        var encoder = new FrontendArtifactEncoder();
        var acceptedBytes = encoder.Encode(
            new FrontendArtifactSnapshotter().Capture(artifact)).Length;
        var stager = new FrontendArtifactStager(resolver,
            new FrontendArtifactEligibilityClassifier(), new FrontendArtifactSnapshotter(),
            encoder);
        stager.Stage(artifact.Analysis.Method, artifact.StructuredMethod);
        state.Active.Analyses[artifact.Analysis.Method.CanonicalName] = artifact.Analysis;
        stager.Stage(artifact.Analysis.Method, artifact.StructuredMethod);
        Assert.Single(state.Active.Staged);
        Assert.Single(state.Active.StagedObjects);
        Assert.Equal(1, state.Active.StagedArtifacts);
        Assert.Equal(acceptedBytes, state.Active.StagedBytes);
        Assert.Throws<ArgumentNullException>(() => stager.Stage(null!, artifact.StructuredMethod));
        Assert.Throws<ArgumentNullException>(() => stager.Stage(artifact.Analysis.Method, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new FrontendAnalysisRecorder(resolver).Record(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new FrontendStructuredMethodRestorer(resolver).TryRestore(null!, out _));
    }

    [Fact]
    public void WorkingSetBudgetAggregatesEncodedAndStructuralAdmissions()
    {
        var budget = new FrontendArtifactWorkingSetBudget();

        Assert.True(budget.TryReserve(
            FrontendArtifactCachePolicy.MaximumArtifacts - 1,
            FrontendArtifactCachePolicy.MaximumEncodedWorkingSetBytes - 1,
            FrontendArtifactCachePolicy.MaximumStructuralWorkingSetBytes - 1));
        Assert.False(budget.TryReserve(0, 2, 0));
        Assert.False(budget.TryReserve(0, 0, 2));
        Assert.False(budget.TryReserve(2, 0, 0));
        Assert.True(budget.TryReserve(1, 1, 1));
        Assert.Equal((
            FrontendArtifactCachePolicy.MaximumArtifacts,
            FrontendArtifactCachePolicy.MaximumEncodedWorkingSetBytes,
            FrontendArtifactCachePolicy.MaximumStructuralWorkingSetBytes),
            budget.Read());
        Assert.False(budget.TryReserve(1, 0, 0));
        Assert.False(budget.TryReserve(-1, 0, 0));
    }

    [Fact]
    public void ContextEligibilityRejectsEveryEntryAssemblyReferenceShape()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var method = artifact.Analysis.Method;
        var entry = new AssemblyIdentity("Entry");
        var context = new FrontendArtifactCacheContext("namespace", entry,
            ImmutableDictionary<string, string>.Empty.Add(
                method.Definition.Key.Assembly.Name, "content"), null);
        var entryType = CliTypeIdentity.Named(entry, "Fixture", "EntryType", false);
        Assert.True(context.IsEligible(method));
        Assert.False(context.IsEligible(method with { DeclaringType = entryType }));
        Assert.False(context.IsEligible(method with { MethodArguments = [entryType] }));
        Assert.False(context.IsEligible(method with
        {
            DeclaringType = CliTypeIdentity.GenericInstantiation(method.DeclaringType, [entryType]),
        }));
        Assert.False(context.IsEligible(method with
        {
            DeclaringType = CliTypeIdentity.SzArray(entryType),
        }));
        Assert.False(context.IsEligible(method with
        {
            DeclaringType = method.DeclaringType.WithStackStorageType(entryType),
        }));
        Assert.False(context.IsEligible(method with
        {
            Definition = method.Definition with
            {
                Key = new(entry, method.Definition.Key.MetadataToken),
            },
        }));
        Assert.False(new FrontendArtifactCacheContext("namespace", entry,
            ImmutableDictionary<string, string>.Empty, null).IsEligible(method));
    }

    [Fact]
    public async Task PayloadReaderHandlesConcurrentBundleLoadAndMissingFiles()
    {
        var state = new FrontendArtifactCacheState();
        var resolver = new FrontendArtifactCacheRequestResolver(state);
        var context = Context(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var factory = new FrontendArtifactCacheRequestFactory(state,
            new FixedIdentityBuilder(context), new RecordingPublisher(),
            new RecordingObjectPublisher());
        using var request = factory.Begin(Options(enabled: true));
        var memory = new FrontendArtifactMemoryStore();
        var reader = new FrontendArtifactPayloadReader(memory, resolver);
        Assert.False(reader.TryRead(new(context, new string('a', 64)), out _));
        var payload = ImmutableArray.Create<byte>(9);
        Monitor.Enter(memory.Gate);
        try
        {
            var pending = Task.Run(() => reader.TryRead(
                new(context, new string('b', 64)), out _));
            Thread.Sleep(20);
            memory.Payloads[context.Namespace + "/" + new string('b', 64)] = payload;
            Monitor.Exit(memory.Gate);
            Assert.True(await pending);
        }
        finally
        {
            if (Monitor.IsEntered(memory.Gate)) Monitor.Exit(memory.Gate);
        }
    }

    [Fact]
    public void AnalyzerFactoryDecoratesCreatedAnalyzer()
    {
        var artifact = FrontendArtifactSnapshotTests.CreateArtifact();
        var inner = new RecordingAnalyzer(artifact.Analysis);
        var factory = new CachingReachableMethodAnalyzerFactory(
            new FixedAnalyzerFactory(inner), new FixedRestorer(artifact),
            new RecordingRecorder());

        var analyzer = factory.Create(null!, null!, null!, null!, null!, null!, null!);

        Assert.Same(artifact.Analysis,
            analyzer.Analyze(new(artifact.Analysis.Method)));
        Assert.Equal(0, inner.CallCount);
        Assert.Throws<ArgumentNullException>(() =>
            new CachingReachableMethodAnalyzerFactory(null!, new FixedRestorer(null),
                new RecordingRecorder()));
        Assert.Throws<ArgumentNullException>(() =>
            new CachingReachableMethodAnalyzerFactory(new FixedAnalyzerFactory(inner), null!,
                new RecordingRecorder()));
        Assert.Throws<ArgumentNullException>(() =>
            new CachingReachableMethodAnalyzerFactory(new FixedAnalyzerFactory(inner),
                new FixedRestorer(null), null!));
    }

    private static ImmutableArray<byte> CompileBindingFixture(string literal) =>
        CompileBindingFixtureSource(
            $"public static class Program {{ public static string Main() => \"{literal}\"; }}");

    private static ImmutableArray<byte> CompileBindingFixtureSource(string source)
    {
        var compilation = global::Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "BindingFixture",
            [global::Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
            [global::Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new global::Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                global::Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: global::Microsoft.CodeAnalysis.OptimizationLevel.Release,
                deterministic: true));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return [.. stream.ToArray()];
    }

    private static CompilerOptions Options(bool enabled) => new(
        "entry.dll", [], "Program", "Main", [], EnableFrontendCache: enabled);

    private static FrontendArtifactCacheContext Context(string? directory) => new(
        "namespace", new("Entry"),
        ImmutableDictionary<string, string>.Empty, directory);

    private static FrontendArtifactCacheTransport CreateTransport(
        IFrontendArtifactCacheIdentityBuilder identities,
        FrontendArtifactMemoryStore memory,
        FrontendArtifactObjectStore objects,
        FrontendArtifactTransportStore store) => new(
            new FrontendArtifactCachePreparationFactory(identities, store),
            new FrontendArtifactCompilationFactory(memory, objects, store),
            new FrontendArtifactPreparationCanceler(store),
            new FrontendArtifactPublicationFactory(store),
            new FrontendArtifactPublicationBatchReader(store),
            new FrontendArtifactPublicationBatchAcknowledger(store),
            new FrontendArtifactPublicationAbandoner(store));

    private static byte[] Mutate(byte[] value, int offset, byte replacement)
    {
        var copy = (byte[])value.Clone();
        copy[offset] = replacement;
        return copy;
    }

    private static byte[] ReplaceInt32(byte[] value, int offset, int replacement)
    {
        var copy = (byte[])value.Clone();
        BitConverter.GetBytes(replacement).CopyTo(copy, offset);
        return copy;
    }

    private static byte[] DuplicateBundleEntry(byte[] value)
    {
        var copy = new byte[value.Length + value.Length - 8];
        value.CopyTo(copy, 0);
        value.AsSpan(8).CopyTo(copy.AsSpan(value.Length));
        BitConverter.GetBytes(2).CopyTo(copy, 4);
        return copy;
    }

    private sealed class RecordingAnalyzer(ReachableMethodAnalysis result) : IReachableMethodAnalyzer
    {
        public int CallCount { get; private set; }
        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        { CallCount++; return result; }
    }

    private sealed class FixedAnalyzerFactory(IReachableMethodAnalyzer analyzer) :
        IReachableMethodAnalyzerFactory
    {
        public IReachableMethodAnalyzer Create(
            NetWasm.Compiler.Metadata.IMethodBodyReader methodBodies,
            ITypeRepository types,
            IFieldRepository fields,
            IMethodRepository methods,
            IMethodSpecializer specializer,
            IImplicitExceptionDiscovery exceptionDiscovery,
            IReachabilityInstructionAnalyzer instructionAnalyzer) => analyzer;
    }

    private sealed class FixedRestorer(FrontendArtifact? artifact) : IFrontendArtifactRestorer
    {
        public bool TryRestore(MethodInstanceModel method, out FrontendArtifact result)
        { result = artifact!; return artifact is not null; }
    }

    private sealed class RecordingRecorder : IFrontendAnalysisRecorder
    {
        public ReachableMethodAnalysis? Analysis { get; private set; }
        public void Record(ReachableMethodAnalysis analysis) => Analysis = analysis;
    }

    private sealed class RecordingLowerer(StructuredMethod result) : IWasmMethodLowerer
    {
        public int CallCount { get; private set; }
        public StructuredMethod Lower(ManagedMethodBody method) { CallCount++; return result; }
    }

    private sealed class FixedStructuredRestorer(StructuredMethod? structured) : IFrontendStructuredMethodRestorer
    {
        public bool TryRestore(MethodInstanceModel method, out StructuredMethod result)
        { result = structured!; return structured is not null; }
    }

    private sealed class RecordingStager : IFrontendArtifactStager
    {
        public StructuredMethod? Structured { get; private set; }
        public void Stage(MethodInstanceModel method, StructuredMethod structured) => Structured = structured;
    }

    private sealed class NullIdentityBuilder : IFrontendArtifactCacheIdentityBuilder
    { public FrontendArtifactCacheContext? Build(CompilerOptions options) => null; }

    private sealed class FixedIdentityBuilder(FrontendArtifactCacheContext context) : IFrontendArtifactCacheIdentityBuilder
    { public FrontendArtifactCacheContext? Build(CompilerOptions options) => context; }

    private sealed class FixedCompilerIdentity(string identity) :
        IFrontendArtifactCompilerIdentity
    {
        public string Read() => identity;
    }

    private sealed class CountingIdentityBuilder(FrontendArtifactCacheContext context) :
        IFrontendArtifactCacheIdentityBuilder
    {
        public int BuildCount { get; private set; }
        public FrontendArtifactCacheContext? Build(CompilerOptions options)
        {
            BuildCount++;
            return context;
        }
    }

    private sealed class RecordingPublisher : IFrontendArtifactPayloadPublisher
    {
        public FrontendArtifactPayloadPublication? Publication { get; private set; }
        public void Publish(FrontendArtifactPayloadPublication publication) => Publication = publication;
    }

    private sealed class ThrowingPublisher : IFrontendArtifactPayloadPublisher
    {
        public void Publish(FrontendArtifactPayloadPublication publication) =>
            throw new IOException("fixture");
    }

    private sealed class RecordingObjectPublisher : IFrontendArtifactObjectPublisher
    {
        public FrontendArtifactObjectPublication? Publication { get; private set; }
        public void Publish(FrontendArtifactObjectPublication publication) => Publication = publication;
    }

    private sealed class ForwardingTransportProbe :
        IFrontendArtifactCachePreparationFactory,
        IFrontendArtifactCompilationFactory,
        IFrontendArtifactPreparationCanceler,
        IFrontendArtifactPublicationFactory,
        IFrontendArtifactPublicationBatchReader,
        IFrontendArtifactPublicationBatchAcknowledger,
        IFrontendArtifactPublicationAbandoner
    {
        public List<string> Events { get; } = [];
        public CompilerOptions? Options { get; private set; }
        public FrontendArtifactCacheDescriptor? Descriptor { get; set; }
        public FrontendArtifactCacheDescriptor? SuppliedDescriptor { get; private set; }
        public IReadOnlyList<FrontendArtifactCacheEntry>? Entries { get; private set; }
        public IDisposable Scope { get; } = new MemoryStream();
        public FrontendArtifactCachePublication? Publication { get; set; }
        public FrontendArtifactCachePublication? SuppliedPublication { get; private set; }
        public FrontendArtifactCacheBatch? Batch { get; set; }
        public FrontendArtifactCacheBatch? SuppliedBatch { get; private set; }
        public Exception? ReadFailure { get; set; }

        public FrontendArtifactCacheDescriptor? Prepare(CompilerOptions options)
        {
            Events.Add("prepare");
            Options = options;
            return Descriptor;
        }

        public IDisposable Begin(FrontendArtifactCacheDescriptor descriptor,
            IReadOnlyList<FrontendArtifactCacheEntry> entries)
        {
            Events.Add("begin");
            SuppliedDescriptor = descriptor;
            Entries = entries;
            return Scope;
        }

        public void Cancel(FrontendArtifactCacheDescriptor descriptor)
        {
            Events.Add("cancel");
            SuppliedDescriptor = descriptor;
        }

        public FrontendArtifactCachePublication? Complete(
            FrontendArtifactCacheDescriptor descriptor)
        {
            Events.Add("complete");
            SuppliedDescriptor = descriptor;
            return Publication;
        }

        public FrontendArtifactCacheBatch Read(
            FrontendArtifactCachePublication publication)
        {
            Events.Add("read");
            SuppliedPublication = publication;
            if (ReadFailure is not null) throw ReadFailure;
            return Batch!;
        }

        public void Acknowledge(FrontendArtifactCachePublication publication,
            FrontendArtifactCacheBatch batch)
        {
            Events.Add("acknowledge");
            SuppliedPublication = publication;
            SuppliedBatch = batch;
        }

        public void Abandon(FrontendArtifactCachePublication publication)
        {
            Events.Add("abandon");
            SuppliedPublication = publication;
        }
    }

    private sealed class OversizeEncoder : IFrontendArtifactEncoder
    {
        public ImmutableArray<byte> Encode(FrontendArtifactSnapshot snapshot) =>
            ImmutableArray.Create(new byte[4 * 1024 * 1024 + 1]);
    }
}
