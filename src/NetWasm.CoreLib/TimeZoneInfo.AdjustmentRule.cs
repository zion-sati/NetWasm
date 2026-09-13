// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System
{
    public sealed partial class TimeZoneInfo
    {
        [Serializable]
        public sealed class AdjustmentRule : IEquatable<AdjustmentRule?>
        {
            private static readonly TimeSpan DaylightDeltaAdjustment = TimeSpan.FromHours(24.0);
            private static readonly TimeSpan MaxDaylightDelta = TimeSpan.FromHours(12.0);
            private readonly DateTime _dateStart;
            private readonly DateTime _dateEnd;
            private readonly TimeSpan _daylightDelta;
            private readonly TransitionTime _daylightTransitionStart;
            private readonly TransitionTime _daylightTransitionEnd;
            private readonly TimeSpan _baseUtcOffsetDelta;   // delta from the default Utc offset (utcOffset = defaultUtcOffset + _baseUtcOffsetDelta)

            public DateTime DateStart => _dateStart;

            public DateTime DateEnd => _dateEnd;

            public TimeSpan DaylightDelta => _daylightDelta;

            public TransitionTime DaylightTransitionStart => _daylightTransitionStart;

            public TransitionTime DaylightTransitionEnd => _daylightTransitionEnd;

            /// <summary>
            /// Gets the time difference with the base UTC offset for the time zone during the adjustment-rule period.
            /// </summary>
            public TimeSpan BaseUtcOffsetDelta => _baseUtcOffsetDelta;

            public bool Equals([NotNullWhen(true)] AdjustmentRule? other) =>
                other != null &&
                _dateStart == other._dateStart &&
                _dateEnd == other._dateEnd &&
                _daylightDelta == other._daylightDelta &&
                _baseUtcOffsetDelta == other._baseUtcOffsetDelta &&
                _daylightTransitionEnd.Equals(other._daylightTransitionEnd) &&
                _daylightTransitionStart.Equals(other._daylightTransitionStart);

            /// <summary>Indicates whether the current instance is equal to another instance.</summary>
            /// <param name="obj">An instance to compare with this instance.</param>
            /// <returns>true if the current instance is equal to the other instance; otherwise, false.</returns>
            public override bool Equals([NotNullWhen(true)] object? obj) =>
                obj is AdjustmentRule other && Equals(other);

            public override int GetHashCode() => _dateStart.GetHashCode();

            private AdjustmentRule(
                DateTime dateStart,
                DateTime dateEnd,
                TimeSpan daylightDelta,
                TransitionTime daylightTransitionStart,
                TransitionTime daylightTransitionEnd,
                TimeSpan baseUtcOffsetDelta,
                bool noDaylightTransitions)
            {
                ValidateAdjustmentRule(dateStart, dateEnd, daylightDelta,
                       daylightTransitionStart, daylightTransitionEnd, noDaylightTransitions);

                _dateStart = dateStart;
                _dateEnd = dateEnd;
                _daylightDelta = daylightDelta;
                _daylightTransitionStart = daylightTransitionStart;
                _daylightTransitionEnd = daylightTransitionEnd;
                _baseUtcOffsetDelta = baseUtcOffsetDelta;
            }

            /// <summary>
            /// Creates a new adjustment rule for a particular time zone.
            /// </summary>
            /// <param name="dateStart">The effective date of the adjustment rule. If the value is <c>DateTime.MinValue.Date</c>, this is the first adjustment rule in effect for a time zone.</param>
            /// <param name="dateEnd">The last date that the adjustment rule is in force. If the value is <c>DateTime.MaxValue.Date</c>, the adjustment rule has no end date.</param>
            /// <param name="daylightDelta">The time change that results from the adjustment. This value is added to the time zone's <see cref="P:System.TimeZoneInfo.BaseUtcOffset" /> and <see cref="P:System.TimeZoneInfo.BaseUtcOffsetDelta" /> properties to obtain the correct daylight offset from Coordinated Universal Time (UTC). This value can range from -14 to 14.</param>
            /// <param name="daylightTransitionStart">The start of daylight saving time.</param>
            /// <param name="daylightTransitionEnd">The end of daylight saving time.</param>
            /// <param name="baseUtcOffsetDelta">The time difference with the base UTC offset for the time zone during the adjustment-rule period.</param>
            /// <returns>The new adjustment rule.</returns>
            public static AdjustmentRule CreateAdjustmentRule(
                DateTime dateStart,
                DateTime dateEnd,
                TimeSpan daylightDelta,
                TransitionTime daylightTransitionStart,
                TransitionTime daylightTransitionEnd,
                TimeSpan baseUtcOffsetDelta)
            {
                return new AdjustmentRule(
                    dateStart,
                    dateEnd,
                    daylightDelta,
                    daylightTransitionStart,
                    daylightTransitionEnd,
                    baseUtcOffsetDelta,
                    noDaylightTransitions: false);
            }

            public static AdjustmentRule CreateAdjustmentRule(
                DateTime dateStart,
                DateTime dateEnd,
                TimeSpan daylightDelta,
                TransitionTime daylightTransitionStart,
                TransitionTime daylightTransitionEnd)
            {
                return new AdjustmentRule(
                    dateStart,
                    dateEnd,
                    daylightDelta,
                    daylightTransitionStart,
                    daylightTransitionEnd,
                    baseUtcOffsetDelta: TimeSpan.Zero,
                    noDaylightTransitions: false);
            }

            internal static AdjustmentRule CreateAdjustmentRule(
                DateTime dateStart,
                DateTime dateEnd,
                TimeSpan daylightDelta,
                TransitionTime daylightTransitionStart,
                TransitionTime daylightTransitionEnd,
                TimeSpan baseUtcOffsetDelta,
                bool noDaylightTransitions)
            {
                AdjustDaylightDeltaToExpectedRange(ref daylightDelta, ref baseUtcOffsetDelta);
                return new AdjustmentRule(
                    dateStart,
                    dateEnd,
                    daylightDelta,
                    daylightTransitionStart,
                    daylightTransitionEnd,
                    baseUtcOffsetDelta,
                    noDaylightTransitions);
            }

            // NetWasm's reflection-free timezone asset represents offset-only periods
            // as adjustment rules without daylight transitions.
            internal static AdjustmentRule CreateFixedOffsetRule(
                DateTime dateStart,
                DateTime dateEnd,
                TimeSpan baseUtcOffsetDelta) =>
                CreateAdjustmentRule(
                    dateStart,
                    dateEnd,
                    daylightDelta: TimeSpan.Zero,
                    daylightTransitionStart: default,
                    daylightTransitionEnd: default,
                    baseUtcOffsetDelta,
                    noDaylightTransitions: true);

            /// <summary>
            /// Helper function that performs all of the validation checks for the factory methods.
            /// </summary>
            private static void ValidateAdjustmentRule(
                DateTime dateStart,
                DateTime dateEnd,
                TimeSpan daylightDelta,
                TransitionTime daylightTransitionStart,
                TransitionTime daylightTransitionEnd,
                bool noDaylightTransitions)
            {
                if (dateStart.Kind != DateTimeKind.Unspecified && dateStart.Kind != DateTimeKind.Utc)
                {
                    throw new ArgumentException(
                        "The DateTime.Kind property of the dateStart parameter must be DateTimeKind.Unspecified or DateTimeKind.Utc.",
                        nameof(dateStart));
                }

                if (dateEnd.Kind != DateTimeKind.Unspecified && dateEnd.Kind != DateTimeKind.Utc)
                {
                    throw new ArgumentException(
                        "The DateTime.Kind property of the dateEnd parameter must be DateTimeKind.Unspecified or DateTimeKind.Utc.",
                        nameof(dateEnd));
                }

                if (daylightTransitionStart.Equals(daylightTransitionEnd) && !noDaylightTransitions)
                {
                    throw new ArgumentException(
                        "The start and end transition times cannot be the same.",
                        nameof(daylightTransitionEnd));
                }

                if (dateStart > dateEnd)
                {
                    throw new ArgumentException(
                        "The dateStart parameter must be less than or equal to the dateEnd parameter.",
                        nameof(dateStart));
                }

                // This cannot use a fourteen-hour bound to account for the scenario where Samoa moved across the International Date Line,
                // which caused their current BaseUtcOffset to be +13. But on the other side of the line it was UTC-11 (+1 for daylight).
                // So when trying to describe DaylightDeltas for those times, the DaylightDelta needs to be -23 (what it takes to go from UTC+13 to UTC-10).
                if (daylightDelta.TotalHours < -23.0 || daylightDelta.TotalHours > 14.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(daylightDelta),
                        daylightDelta,
                        "The daylight delta must be within the range of -23 to 14 hours.");
                }

                if (daylightDelta.Ticks % TimeSpan.TicksPerMinute != 0)
                {
                    throw new ArgumentException(
                        "The daylight delta cannot contain seconds.",
                        nameof(daylightDelta));
                }

                if (dateStart != DateTime.MinValue && dateStart.Kind == DateTimeKind.Unspecified && dateStart.TimeOfDay != TimeSpan.Zero)
                {
                    throw new ArgumentException(
                        "The dateStart parameter cannot contain a time of day.",
                        nameof(dateStart));
                }

                if (dateEnd != DateTime.MaxValue && dateEnd.Kind == DateTimeKind.Unspecified && dateEnd.TimeOfDay != TimeSpan.Zero)
                {
                    throw new ArgumentException(
                        "The dateEnd parameter cannot contain a time of day.",
                        nameof(dateEnd));
                }
            }

            /// <summary>
            /// Ensures the daylight delta is within [-12, 12] hours
            /// </summary>>
            private static void AdjustDaylightDeltaToExpectedRange(ref TimeSpan daylightDelta, ref TimeSpan baseUtcOffsetDelta)
            {
                if (daylightDelta > MaxDaylightDelta)
                {
                    daylightDelta -= DaylightDeltaAdjustment;
                    baseUtcOffsetDelta += DaylightDeltaAdjustment;
                }
                else if (daylightDelta < -MaxDaylightDelta)
                {
                    daylightDelta += DaylightDeltaAdjustment;
                    baseUtcOffsetDelta -= DaylightDeltaAdjustment;
                }
            }
        }
    }
}
