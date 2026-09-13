namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct TimeZoneTransition
    {
        internal TimeZoneTransition(long unixSeconds, int offsetSeconds, bool isDaylightSavingTime)
        {
            UnixSeconds = unixSeconds;
            OffsetSeconds = offsetSeconds;
            IsDaylightSavingTime = isDaylightSavingTime;
        }

        internal long UnixSeconds { get; }
        internal int OffsetSeconds { get; }
        internal bool IsDaylightSavingTime { get; }
    }
}
