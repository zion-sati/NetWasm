namespace System.Runtime.InteropServices
{
    internal sealed class WasiMonotonicClock : IPlatformMonotonicClock
    {
        private readonly IPlatformMonotonicClockSource _source;

        internal WasiMonotonicClock(IPlatformMonotonicClockSource source)
        {
            _source = source ?? throw new ArgumentNullException();
        }

        public ulong GetMonotonicTime() => _source.GetMonotonicTime();
    }

    internal sealed class WasiMonotonicClockSource : IPlatformMonotonicClockSource
    {
        public ulong GetMonotonicTime() =>
            unchecked((ulong)WasiClockImports.GetMonotonicTime());
    }
}
