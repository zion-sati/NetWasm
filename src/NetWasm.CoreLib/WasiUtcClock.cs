namespace System.Runtime.InteropServices
{
    using WebAssembly;

    internal sealed class WasiUtcClock : IPlatformUtcClock
    {
        private readonly IPlatformUtcClockSource _source;

        internal WasiUtcClock(IPlatformUtcClockSource source)
        {
            _source = source ?? throw new ArgumentNullException();
        }

        public PlatformTimestamp GetUtcTime() => _source.GetUtcTime();
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
