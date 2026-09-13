// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System
{
    public sealed partial class TimeZoneInfo
    {
        [Serializable]
        public readonly struct TransitionTime : IEquatable<TransitionTime>
        {
            private readonly DateTime _timeOfDay;
            private readonly byte _month;
            private readonly byte _week;
            private readonly byte _day;
            private readonly DayOfWeek _dayOfWeek;
            private readonly bool _isFixedDateRule;

            public DateTime TimeOfDay => _timeOfDay;

            public int Month => _month;

            public int Week => _week;

            public int Day => _day;

            public DayOfWeek DayOfWeek => _dayOfWeek;

            public bool IsFixedDateRule => _isFixedDateRule;

            public override bool Equals([NotNullWhen(true)] object? obj) =>
                obj is TransitionTime && Equals((TransitionTime)obj);

            public static bool operator ==(TransitionTime t1, TransitionTime t2) => t1.Equals(t2);

            public static bool operator !=(TransitionTime t1, TransitionTime t2) => !t1.Equals(t2);

            public bool Equals(TransitionTime other) =>
                _isFixedDateRule == other._isFixedDateRule &&
                _timeOfDay == other._timeOfDay &&
                _month == other._month &&
                (other._isFixedDateRule ?
                    _day == other._day :
                    _week == other._week && _dayOfWeek == other._dayOfWeek);

            public override int GetHashCode() => (int)_month ^ (int)_week << 8;

            private TransitionTime(DateTime timeOfDay, int month, int week, int day, DayOfWeek dayOfWeek, bool isFixedDateRule)
            {
                ValidateTransitionTime(timeOfDay, month, week, day, dayOfWeek);

                _timeOfDay = timeOfDay;
                _month = (byte)month;
                _week = (byte)week;
                _day = (byte)day;
                _dayOfWeek = dayOfWeek;
                _isFixedDateRule = isFixedDateRule;
            }

            public static TransitionTime CreateFixedDateRule(DateTime timeOfDay, int month, int day) =>
                new TransitionTime(timeOfDay, month, 1, day, DayOfWeek.Sunday, isFixedDateRule: true);

            public static TransitionTime CreateFloatingDateRule(DateTime timeOfDay, int month, int week, DayOfWeek dayOfWeek) =>
                new TransitionTime(timeOfDay, month, week, 1, dayOfWeek, isFixedDateRule: false);

            /// <summary>
            /// Helper function that validates a TransitionTime instance.
            /// </summary>
            private static void ValidateTransitionTime(DateTime timeOfDay, int month, int week, int day, DayOfWeek dayOfWeek)
            {
                if (timeOfDay.Kind != DateTimeKind.Unspecified)
                {
                    throw new ArgumentException(
                        "The DateTime.Kind property of the timeOfDay parameter must be DateTimeKind.Unspecified.",
                        nameof(timeOfDay));
                }

                // Month range 1-12
                if (month < 1 || month > 12)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(month),
                        "The month parameter must be between 1 and 12.");
                }

                // Day range 1-31
                if (day < 1 || day > 31)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(day),
                        "The day parameter must be between 1 and 31.");
                }

                // Week range 1-5
                if (week < 1 || week > 5)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(week),
                        "The week parameter must be between 1 and 5.");
                }

                // DayOfWeek range 0-6
                if ((int)dayOfWeek < 0 || (int)dayOfWeek > 6)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(dayOfWeek),
                        "The dayOfWeek parameter must be between Sunday and Saturday.");
                }

                if (timeOfDay.Ticks >= TimeSpan.TicksPerDay || (ulong)timeOfDay.Ticks % TimeSpan.TicksPerMillisecond != 0)
                {
                    throw new ArgumentException(
                        "The timeOfDay parameter cannot contain ticks smaller than a millisecond.",
                        nameof(timeOfDay));
                }
            }
        }
    }
}
