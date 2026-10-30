namespace System.Runtime.InteropServices
{
    using WebAssembly;

    // Compatibility adapter for the historical aggregate clock contract used
    // by existing CoreLib fixtures. The actual platform actors are the two
    // one-action clocks injected below.
    internal sealed class WasiClockBackend : IPlatformClock
    {
        private readonly IPlatformUtcClock _utcClock;
        private readonly IPlatformMonotonicClock _monotonicClock;

        internal WasiClockBackend(
            IPlatformUtcClock utcClock,
            IPlatformMonotonicClock monotonicClock)
        {
            _utcClock = utcClock ?? throw new ArgumentNullException();
            _monotonicClock = monotonicClock ?? throw new ArgumentNullException();
        }

        PlatformTimestamp IPlatformUtcClock.GetUtcTime()
            => _utcClock.GetUtcTime();

        ulong IPlatformMonotonicClock.GetMonotonicTime() =>
            _monotonicClock.GetMonotonicTime();
    }

    internal static class WasiClockImports
    {
        [WitImport("wasi:clocks/wall-clock@0.2.11", "now")]
        internal static extern void GetUtcTime(nuint result);

        [WitImport("wasi:clocks/wall-clock@0.2.11", "resolution")]
        internal static extern void GetUtcResolution(nuint result);

        [WitImport("wasi:clocks/monotonic-clock@0.2.11", "now")]
        internal static extern long GetMonotonicTime();

        [WitImport("wasi:clocks/monotonic-clock@0.2.11", "resolution")]
        internal static extern long GetMonotonicResolution();

        [WitImport("wasi:clocks/monotonic-clock@0.2.11", "subscribe-instant")]
        internal static extern int SubscribeInstant(long instant);

        [WitImport("wasi:clocks/monotonic-clock@0.2.11", "subscribe-duration")]
        internal static extern int SubscribeDuration(long duration);
    }

    internal static class WasiPollImports
    {
        [WitImport("wasi:io/poll@0.2.11", "[resource-drop]pollable")]
        internal static extern void Drop(int handle);

        [WitImport("wasi:io/poll@0.2.11", "[method]pollable.ready")]
        internal static extern int IsReady(int handle);

        [WitImport("wasi:io/poll@0.2.11", "[method]pollable.block")]
        internal static extern void Block(int handle);

        [WitImport("wasi:io/poll@0.2.11", "poll")]
        internal static extern void Poll(
            nuint handles,
            nuint handleCount,
            nuint result);
    }

}
