namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneDefinition
    {
        internal TimeZoneDefinition(
            string name,
            int initialOffsetSeconds,
            bool initialDaylightSavingTime,
            TimeZoneTransition[] transitions)
        {
            Name = name ?? throw new ArgumentNullException();
            Transitions = transitions ?? throw new ArgumentNullException();
            InitialOffsetSeconds = initialOffsetSeconds;
            InitialDaylightSavingTime = initialDaylightSavingTime;
        }

        internal string Name { get; }
        internal int InitialOffsetSeconds { get; }
        internal bool InitialDaylightSavingTime { get; }
        internal TimeZoneTransition[] Transitions { get; }
    }
}
