using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

// What a worker is composed with is built beside it; a failure releases the worker too.
public sealed class ManagedBodyWorkerCompositionTests
{
    [Fact]
    public void WhatIsBuiltBesideAWorkerLeavesItInUse()
    {
        var worker = new Worker();

        Assert.Equal(7, ManagedBodyWorkerCompositionRoot.Beside(worker, () => 7));
        Assert.False(worker.Disposed);
    }

    [Fact]
    public void AWorkerIsReleasedWhenWhatIsBuiltBesideItFails()
    {
        var worker = new Worker();
        var failure = new InvalidOperationException("replay");

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            ManagedBodyWorkerCompositionRoot.Beside<int>(worker, () => throw failure)));
        Assert.True(worker.Disposed);
    }

    private sealed class Worker : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
