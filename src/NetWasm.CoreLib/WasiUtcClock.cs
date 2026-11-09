namespace System.Runtime.InteropServices
{
    using WebAssembly;

    // wasi:clocks/system-clock as DateTime.UtcNow's clock.
    internal sealed class WasiUtcClock : ISystemClock
    {
        // 1970-01-01T00:00:00Z, the epoch of WASI's system clock, in DateTime ticks.
        private const long UnixEpochTicks = 621_355_968_000_000_000;

        private readonly IPlatformUtcClockSource _source;

        internal WasiUtcClock(IPlatformUtcClockSource source)
        {
            _source = source ?? throw new ArgumentNullException();
        }

        public DateTime GetUtcNow()
        {
            var timestamp = _source.GetUtcTime();
            if (timestamp.Seconds > long.MaxValue)
            {
                throw new OverflowException();
            }
            return new DateTime(
                checked(
                    UnixEpochTicks
                    + (long)timestamp.Seconds * TimeSpan.TicksPerSecond
                    + timestamp.Nanoseconds / 100),
                DateTimeKind.Utc);
        }
    }

    internal sealed class WasiUtcClockSource : IPlatformUtcClockSource
    {
        public PlatformTimestamp GetUtcTime()
        {
            var result = CanonicalAbi.Allocate(16, 8);
            try
            {
                WasiClockImports.GetUtcTime(result);
                return new PlatformTimestamp(
                    unchecked((ulong)CanonicalAbi.ReadInt64(result, 0)),
                    unchecked((uint)CanonicalAbi.ReadInt32(result, 8)));
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }
    }
}
