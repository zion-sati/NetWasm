namespace System.Runtime.InteropServices.Concurrency;

// The delay of a component whose world does not import wasi:clocks/monotonic-clock: there is
// no clock to wait on, so the delay throws instead of never completing.
internal sealed class AbsentMonotonicWaitInvoker : IWasiMonotonicWaitInvoker
{
    public int WaitFor(ulong nanoseconds) => throw new PlatformNotSupportedException(
        "A delay needs wasi:clocks/monotonic-clock, which the selected WIT world does not import.");
}
