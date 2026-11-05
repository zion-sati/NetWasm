using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedBodyBatchEmitterFactoryTests
{
    [Fact]
    public void LayoutForkContractsRejectMissingCapabilities()
    {
        var values = new UnusedValueLayouts();
        var fields = new UnusedFieldLayouts();
        var publisher = new NoOpPublisher();

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedLayoutFork(null!, fields));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedLayoutFork(values, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedLayoutForks([], null!));
        Assert.Empty(new ManagedLayoutForks([], publisher).Workers);
    }

    [Fact]
    public void DisposesTheBuiltWorkersWhenAConstructionFails()
    {
        var source = new FakeForkSource();
        var workers = new FakeWorkerFactory(source) { FailingForks = [2] };
        var factory = new ManagedBodyBatchEmitterFactory(source, 4, workers);

        Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Equal(4, source.RequestedCount);
        // The workers are built side by side, so every one is attempted.
        Assert.Equal(4, workers.Calls);
        Assert.Equal([true, true, true], workers.Disposables.Select(item =>
            item.Disposed));
    }

    [Fact]
    public void ThrowsTheFirstConstructionFailureInWorkerOrder()
    {
        var source = new FakeForkSource();
        var workers = new FakeWorkerFactory(source) { FailingForks = [3, 1] };
        var factory = new ManagedBodyBatchEmitterFactory(source, 4, workers);

        var failure = Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Equal("factory failure 1", failure.Message);
        Assert.Equal([true, true], workers.Disposables.Select(item =>
            item.Disposed));
    }

    [Fact]
    public void BatchOwnsAndDisposesEveryBuiltWorker()
    {
        var source = new FakeForkSource();
        var workers = new FakeWorkerFactory(source);
        var factory = new ManagedBodyBatchEmitterFactory(source, 2, workers);

        using (factory.Create())
        {
            Assert.Equal([false, false], workers.Disposables.Select(item =>
                item.Disposed));
        }

        Assert.Equal([true, true], workers.Disposables.Select(item =>
            item.Disposed));
    }

    [Fact]
    public void BatchRethrowsTheFirstMethodFailureWithoutPublishingForks()
    {
        var publisher = new RecordingPublisher();
        var provider = new TrackedDisposable();
        var worker = new ManagedBodyWorker(provider, null!, null!,
            new MethodBufferedLogger(
                NullLogger<WasmModuleEmitterFactory>.Instance));
        var batch = new ManagedBodyBatchEmitter(
            new ManagedLayoutForks([], publisher), [worker]);
        var key = new EntityKey(new AssemblyIdentity("Failure"), 1);

        Assert.Throws<KeyNotFoundException>(() => batch.Emit(
            [new ManagedDefinitionEmission(new ManagedMethodIdentity("Failure::Run"), key)],
            new Dictionary<EntityKey,
                NetWasm.Compiler.ControlFlow.Structured.StructuredMethod>(),
            new Dictionary<EntityKey, MethodRootMap>(),
            null!,
            null!));

        Assert.False(publisher.Published);
        batch.Dispose();
        Assert.True(provider.Disposed);
    }

    [Fact]
    public void WorkerResolutionDisposesItsOwnerWhenARequiredServiceIsMissing()
    {
        var owner = new TrackedDisposable();
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            ManagedBodyWorkerCompositionRoot.Resolve(
                provider, owner,
                new MethodBufferedLogger(
                    NullLogger<WasmModuleEmitterFactory>.Instance)));

        Assert.True(owner.Disposed);
    }

    private sealed class FakeForkSource : IManagedLayoutForkSource
    {
        public int RequestedCount { get; private set; }
        public ImmutableArray<ManagedLayoutFork> Forks { get; private set; } = [];

        public ManagedLayoutForks Create(int workerCount)
        {
            RequestedCount = workerCount;
            Forks = [.. Enumerable.Range(0, workerCount)
                .Select(_ => new ManagedLayoutFork(
                    new UnusedValueLayouts(), new UnusedFieldLayouts()))];
            return new(Forks, new NoOpPublisher());
        }
    }

    private sealed class NoOpPublisher : IManagedLayoutForkPublisher
    {
        public void Publish() { }
    }

    private sealed class RecordingPublisher : IManagedLayoutForkPublisher
    {
        public bool Published { get; private set; }
        public void Publish() => Published = true;
    }

    private sealed class UnusedValueLayouts : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedFieldLayouts : IInstanceFieldLayoutProvider
    {
        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            throw new NotSupportedException();

        public FieldLayout GetFieldLayout(EntityKey field) =>
            throw new NotSupportedException();
    }

    // Called from several threads at once, as the batch factory builds its workers in parallel.
    private sealed class FakeWorkerFactory(FakeForkSource source) : IManagedBodyWorkerFactory
    {
        private int _calls;

        public System.Collections.Concurrent.ConcurrentQueue<TrackedDisposable> Disposables { get; } = new();
        public int Calls => Volatile.Read(ref _calls);
        public int[] FailingForks { get; init; } = [];

        public ManagedBodyWorker Create(ManagedLayoutFork fork)
        {
            Interlocked.Increment(ref _calls);
            var index = source.Forks.IndexOf(fork);
            if (FailingForks.Contains(index))
                throw new InvalidOperationException($"factory failure {index}");
            var disposable = new TrackedDisposable();
            Disposables.Enqueue(disposable);
            return new(disposable, null!, null!,
                new MethodBufferedLogger(
                    NullLogger<WasmModuleEmitterFactory>.Instance));
        }
    }

    private sealed class TrackedDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
