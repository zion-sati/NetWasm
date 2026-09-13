namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct LocalTimeOffset
    {
        internal LocalTimeOffset(long ticks, bool isDaylightSavingTime)
        {
            Ticks = ticks;
            IsDaylightSavingTime = isDaylightSavingTime;
        }

        internal long Ticks { get; }

        internal bool IsDaylightSavingTime { get; }
    }
}
