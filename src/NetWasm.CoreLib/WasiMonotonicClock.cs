namespace System.Runtime.InteropServices
{
    using WebAssembly;

    // wasi:clocks/monotonic-clock as Stopwatch's clock: nanoseconds.
    internal sealed class WasiMonotonicClock : IMonotonicClock
    {
        private readonly IPlatformMonotonicClockSource _source;

        internal WasiMonotonicClock(IPlatformMonotonicClockSource source)
        {
            _source = source ?? throw new ArgumentNullException();
        }

        public long GetTimestamp() => unchecked((long)_source.GetMonotonicTime());
    }

    internal sealed class WasiMonotonicClockSource : IPlatformMonotonicClockSource
    {
        public ulong GetMonotonicTime() =>
            unchecked((ulong)WasiClockImports.GetMonotonicTime());
    }

    internal static class WasiClockImports
    {
        [WitImport("wasi:clocks/system-clock@0.3.2", "now")]
        internal static extern void GetUtcTime(nuint result);

        [WitImport("wasi:clocks/monotonic-clock@0.3.2", "now")]
        internal static extern long GetMonotonicTime();
    }
}
