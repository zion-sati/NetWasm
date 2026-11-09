namespace System.Runtime.InteropServices.Concurrency;

// Synchronous waits for the platform services' blocking APIs. No managed code
// runs while waitable-set.wait blocks, so waits never nest and one set, created
// on first use, serves every wait. Only an async-typed task may block: inside a
// synchronous WIT export, before it returns, the wait throws instead of letting
// the host trap. A host that cannot suspend the guest (NetWasm's raw JavaScript
// host) answers a wait with no event, and the wait throws.
internal sealed class WaitableEventAwaiter : IWaitableEventAwaiter
{
    private const int EventNone = 0;

    private readonly IWasiWaitableSetCreateInvoker _create;
    private readonly IWasiWaitableJoinInvoker _join;
    private readonly IWasiWaitableSetWaitInvoker _wait;
    private int _set;

    internal WaitableEventAwaiter(
        IWasiWaitableSetCreateInvoker create,
        IWasiWaitableJoinInvoker join,
        IWasiWaitableSetWaitInvoker wait)
    {
        _create = create ?? throw new ArgumentNullException();
        _join = join ?? throw new ArgumentNullException();
        _wait = wait ?? throw new ArgumentNullException();
    }

    public uint Await(int waitable)
    {
        if (waitable == 0)
        {
            throw new ArgumentOutOfRangeException();
        }

        if (PlatformWorld.HasSynchronousExports() && WebAssembly.SynchronousExportScope.Depth != 0)
        {
            throw new InvalidOperationException(
                "A synchronous WIT export cannot wait for asynchronous I/O before it returns; use the asynchronous API or declare the export an async func.");
        }

        if (_set == 0)
        {
            _set = _create.Create();
        }

        // The built-ins trap rather than throw, so the waitable always leaves the set.
        _join.Join(waitable, _set);
        var waited = _wait.Wait(_set);
        _join.Join(waitable, 0);

        if (waited.Code == EventNone)
        {
            throw new PlatformNotSupportedException(
                "This host cannot suspend the program for a synchronous wait on asynchronous I/O; use the asynchronous API, or a component host.");
        }

        if (waited.Waitable != waitable)
        {
            throw new InvalidOperationException();
        }

        return waited.Payload;
    }
}
