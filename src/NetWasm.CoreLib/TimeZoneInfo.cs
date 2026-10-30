// Adapted from dotnet/runtime System.Private.CoreLib TimeZoneInfo.
// Upstream commit: 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using Collections.Generic;
    using Collections.ObjectModel;
    using Runtime.InteropServices;
    using Runtime.InteropServices.TimeZones;

    /// <summary>
    /// Provides the selected, reflection-free time-zone data exposed by the
    /// NetWasm timezone asset.
    /// </summary>
    public sealed partial class TimeZoneInfo : IEquatable<TimeZoneInfo?>
    {
        private const long UnixEpochTicks = 621355968000000000;
        private const long TicksPerSecond = 10_000_000;
        private const string UtcId = "UTC";

        private static readonly TimeZoneInfo s_utc = CreateUtc();
        private static readonly TransitionTime s_daylightRuleMarker =
            TransitionTime.CreateFixedDateRule(
                DateTime.MinValue.AddMilliseconds(2),
                1,
                1);
        private static TimeZoneInfo? s_local;
        private static TimeZoneCatalog? s_catalog;

        private readonly string _id;
        private readonly string _displayName;
        private readonly string _standardName;
        private readonly string _daylightName;
        private readonly TimeZoneDefinition _definition;
        private readonly ITimeZoneTransitionResolver _transitions;
        private AdjustmentRule[]? _adjustmentRules;
        private readonly bool _supportsDaylightSavingTime;
        private readonly bool _hasIanaId;
        private readonly bool _isUtc;
        private readonly bool _isLocal;

        private TimeZoneInfo(
            string id,
            TimeZoneDefinition definition,
            ITimeZoneTransitionResolver transitions,
            bool isUtc,
            bool isLocal,
            bool hasIanaId,
            AdjustmentRule[]? adjustmentRules = null)
        {
            _id = id ?? throw new ArgumentNullException();
            _definition = definition ?? throw new ArgumentNullException();
            _transitions = transitions ?? throw new ArgumentNullException();
            _isUtc = isUtc;
            _isLocal = isLocal;
            _hasIanaId = hasIanaId;
            _displayName = id;
            _standardName = id;
            _daylightName = id;
            _adjustmentRules = adjustmentRules;
            _supportsDaylightSavingTime = HasDaylightTransitions(definition);
        }

        public string Id => _id;

        public string DisplayName => _displayName;

        public string StandardName => _standardName;

        public string DaylightName => _daylightName;

        public TimeSpan BaseUtcOffset => TimeSpan.FromSeconds(
            NormalizeOffsetToMinute(ResolveBaseUtcOffsetSeconds(_definition)));

        public bool SupportsDaylightSavingTime => _supportsDaylightSavingTime;

        public bool HasIanaId => _hasIanaId;

        public static TimeZoneInfo Utc => s_utc;

        public static TimeZoneInfo Local => s_local ??= CreateLocal();

        public TimeSpan GetUtcOffset(DateTimeOffset dateTimeOffset)
        {
            ArgumentNullException.ThrowIfNull(dateTimeOffset);
            var offset = _transitions.Resolve(
                _definition,
                dateTimeOffset.UtcDateTime.Ticks,
                LocalTimeBasis.Utc);
            return new TimeSpan(offset.Ticks);
        }

        public TimeSpan GetUtcOffset(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified || dateTime.Kind == DateTimeKind.Local && _isLocal)
            {
                var localOffset = _transitions.Resolve(
                    _definition,
                    dateTime.Ticks,
                    LocalTimeBasis.Local);
                return new TimeSpan(localOffset.Ticks);
            }

            var utcTicks = GetUtcTicks(dateTime, throwOnInvalid: false);
            var offset = _transitions.Resolve(_definition, utcTicks, LocalTimeBasis.Utc);
            return new TimeSpan(offset.Ticks);
        }

        public bool IsDaylightSavingTime(DateTimeOffset dateTimeOffset)
        {
            ArgumentNullException.ThrowIfNull(dateTimeOffset);
            return _transitions.Resolve(
                _definition,
                dateTimeOffset.UtcDateTime.Ticks,
                LocalTimeBasis.Utc).IsDaylightSavingTime;
        }

        public bool IsDaylightSavingTime(DateTime dateTime)
        {
            if (!SupportsDaylightSavingTime)
            {
                return false;
            }

            if (dateTime.Kind == DateTimeKind.Unspecified)
            {
                return _transitions.Resolve(
                    _definition,
                    dateTime.Ticks,
                    LocalTimeBasis.Local).IsDaylightSavingTime;
            }

            var utcTicks = GetUtcTicks(dateTime, throwOnInvalid: false);
            return _transitions.Resolve(
                _definition,
                utcTicks,
                LocalTimeBasis.Utc).IsDaylightSavingTime;
        }

        public bool IsAmbiguousTime(DateTimeOffset dateTimeOffset)
        {
            ArgumentNullException.ThrowIfNull(dateTimeOffset);
            var local = ConvertTime(dateTimeOffset, this).DateTime;
            return IsAmbiguousTime(local);
        }

        public bool IsAmbiguousTime(DateTime dateTime)
        {
            if (!SupportsDaylightSavingTime)
            {
                return false;
            }

            var localTicks = GetLocalTicksForInspection(dateTime);
            return TryGetAmbiguousOffsets(localTicks, out _, out _);
        }

        public bool IsInvalidTime(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc || !_isLocal &&
                dateTime.Kind == DateTimeKind.Local)
            {
                return false;
            }

            var localTicks = dateTime.Kind == DateTimeKind.Local
                ? dateTime.Ticks
                : dateTime.Ticks;
            return IsInvalidLocalTicks(localTicks);
        }

        public TimeSpan[] GetAmbiguousTimeOffsets(DateTime dateTime)
        {
            var localTicks = GetLocalTicksForInspection(dateTime);
            if (!TryGetAmbiguousOffsets(localTicks, out var first, out var second))
            {
                throw new ArgumentException(
                    "The supplied time is not ambiguous.",
                    nameof(dateTime));
            }
            return new[] { first, second };
        }

        public TimeSpan[] GetAmbiguousTimeOffsets(DateTimeOffset dateTimeOffset)
        {
            ArgumentNullException.ThrowIfNull(dateTimeOffset);
            return GetAmbiguousTimeOffsets(ConvertTime(dateTimeOffset, this).DateTime);
        }

        public AdjustmentRule[] GetAdjustmentRules() =>
            (AdjustmentRule[])GetOrCreateAdjustmentRules().Clone();

        public bool HasSameRules(TimeZoneInfo other)
        {
            ArgumentNullException.ThrowIfNull(other);
            var rules = GetOrCreateAdjustmentRules();
            var otherRules = other.GetOrCreateAdjustmentRules();
            if (BaseUtcOffset != other.BaseUtcOffset ||
                SupportsDaylightSavingTime != other.SupportsDaylightSavingTime ||
                rules.Length != otherRules.Length)
            {
                return false;
            }

            for (var index = 0; index < rules.Length; index++)
            {
                if (!rules[index].Equals(otherRules[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public static TimeZoneInfo FindSystemTimeZoneById(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (id == UtcId || id == "Etc/UTC")
            {
                return Utc;
            }
            try
            {
                return (s_catalog ??= new TimeZoneCatalog()).Resolve(id);
            }
            catch (TimeZoneAssetValidationException exception)
            {
                throw new InvalidTimeZoneException(
                    "The selected timezone asset is invalid.",
                    exception);
            }
            catch (PlatformNotSupportedException exception)
            {
                throw new TimeZoneNotFoundException(
                    "The timezone is not present in the selected asset.",
                    exception);
            }
        }

        public static bool TryFindSystemTimeZoneById(
            string id,
            out TimeZoneInfo? timeZoneInfo)
        {
            ArgumentNullException.ThrowIfNull(id);
            try
            {
                timeZoneInfo = FindSystemTimeZoneById(id);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                timeZoneInfo = null;
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                timeZoneInfo = null;
                return false;
            }
        }

        public static ReadOnlyCollection<TimeZoneInfo> GetSystemTimeZones() =>
            GetSystemTimeZones(skipSorting: false);

        public static ReadOnlyCollection<TimeZoneInfo> GetSystemTimeZones(bool skipSorting)
        {
            List<TimeZoneInfo> zones;
            try
            {
                zones = (s_catalog ??= new TimeZoneCatalog()).GetAll();
            }
            catch (TimeZoneAssetValidationException exception)
            {
                throw new InvalidTimeZoneException(
                    "The selected timezone asset is invalid.",
                    exception);
            }
            if (!skipSorting)
            {
                zones.Sort(static (left, right) =>
                {
                    var offset = left.BaseUtcOffset.CompareTo(right.BaseUtcOffset);
                    return offset == 0
                        ? string.CompareOrdinal(left.DisplayName, right.DisplayName)
                        : offset;
                });
            }
            return new ReadOnlyCollection<TimeZoneInfo>(zones);
        }

        public static DateTime ConvertTime(DateTime dateTime, TimeZoneInfo destinationTimeZone)
        {
            ArgumentNullException.ThrowIfNull(destinationTimeZone);
            var source = dateTime.Kind == DateTimeKind.Utc ? Utc : Local;
            return ConvertTime(dateTime, source, destinationTimeZone);
        }

        public static DateTime ConvertTime(
            DateTime dateTime,
            TimeZoneInfo sourceTimeZone,
            TimeZoneInfo destinationTimeZone)
        {
            ArgumentNullException.ThrowIfNull(sourceTimeZone);
            ArgumentNullException.ThrowIfNull(destinationTimeZone);

            var sourceKind = sourceTimeZone._isUtc
                ? DateTimeKind.Utc
                : sourceTimeZone._isLocal
                    ? DateTimeKind.Local
                    : DateTimeKind.Unspecified;
            if (dateTime.Kind != DateTimeKind.Unspecified &&
                dateTime.Kind != sourceKind)
            {
                throw new ArgumentException(
                    "The supplied DateTime.Kind does not match the source timezone.",
                    nameof(sourceTimeZone));
            }

            if (sourceTimeZone.IsInvalidTime(dateTime))
            {
                throw new ArgumentException(
                    "The supplied time is invalid in the source timezone.",
                    nameof(dateTime));
            }

            if (dateTime.Kind != DateTimeKind.Unspecified &&
                sourceKind == (destinationTimeZone._isUtc
                    ? DateTimeKind.Utc
                    : destinationTimeZone._isLocal
                        ? DateTimeKind.Local
                        : DateTimeKind.Unspecified))
            {
                return dateTime;
            }

            var utcTicks = sourceTimeZone.GetUtcTicks(dateTime, throwOnInvalid: true);
            var destination = destinationTimeZone._transitions.Resolve(
                destinationTimeZone._definition,
                utcTicks,
                LocalTimeBasis.Utc);
            var targetTicks = utcTicks + destination.Ticks;
            if ((ulong)targetTicks > (ulong)DateTime.MaxTicks)
            {
                targetTicks = targetTicks < 0 ? 0 : DateTime.MaxTicks;
            }
            var kind = destinationTimeZone._isUtc
                ? DateTimeKind.Utc
                : destinationTimeZone._isLocal
                    ? DateTimeKind.Local
                    : DateTimeKind.Unspecified;
            return kind == DateTimeKind.Local
                ? new DateTime(
                    targetTicks,
                    DateTimeKind.Local,
                    destination.IsAmbiguousDaylightSavingTime)
                : new DateTime(targetTicks, kind);
        }

        public static DateTimeOffset ConvertTime(
            DateTimeOffset dateTimeOffset,
            TimeZoneInfo destinationTimeZone)
        {
            ArgumentNullException.ThrowIfNull(destinationTimeZone);
            var utcTicks = dateTimeOffset.UtcDateTime.Ticks;
            var offset = destinationTimeZone._transitions.Resolve(
                destinationTimeZone._definition,
                utcTicks,
                LocalTimeBasis.Utc);
            var targetTicks = utcTicks + offset.Ticks;
            if (targetTicks > DateTime.MaxValue.Ticks)
            {
                return DateTimeOffset.MaxValue;
            }
            if (targetTicks < DateTime.MinValue.Ticks)
            {
                return DateTimeOffset.MinValue;
            }
            return new DateTimeOffset(targetTicks, new TimeSpan(offset.Ticks));
        }

        public static DateTime ConvertTimeFromUtc(
            DateTime dateTime,
            TimeZoneInfo destinationTimeZone) =>
            ConvertTime(dateTime, Utc, destinationTimeZone);

        public static DateTime ConvertTimeToUtc(DateTime dateTime) =>
            ConvertTime(dateTime, Local, Utc);

        public static DateTime ConvertTimeToUtc(
            DateTime dateTime,
            TimeZoneInfo sourceTimeZone) =>
            ConvertTime(dateTime, sourceTimeZone, Utc);

        public static DateTime ConvertTimeBySystemTimeZoneId(
            DateTime dateTime,
            string destinationTimeZoneId) =>
            ConvertTime(dateTime, FindSystemTimeZoneById(destinationTimeZoneId));

        public static DateTime ConvertTimeBySystemTimeZoneId(
            DateTime dateTime,
            string sourceTimeZoneId,
            string destinationTimeZoneId) =>
            ConvertTime(
                dateTime,
                FindSystemTimeZoneById(sourceTimeZoneId),
                FindSystemTimeZoneById(destinationTimeZoneId));

        public static DateTimeOffset ConvertTimeBySystemTimeZoneId(
            DateTimeOffset dateTimeOffset,
            string destinationTimeZoneId) =>
            ConvertTime(dateTimeOffset, FindSystemTimeZoneById(destinationTimeZoneId));

        public static TimeZoneInfo CreateCustomTimeZone(
            string id,
            TimeSpan baseUtcOffset,
            string? displayName,
            string? standardDisplayName) =>
            throw UnsupportedCustomZone();

        public static TimeZoneInfo CreateCustomTimeZone(
            string id,
            TimeSpan baseUtcOffset,
            string? displayName,
            string? standardDisplayName,
            string? daylightDisplayName,
            AdjustmentRule[]? adjustmentRules) =>
            throw UnsupportedCustomZone();

        public static TimeZoneInfo CreateCustomTimeZone(
            string id,
            TimeSpan baseUtcOffset,
            string? displayName,
            string? standardDisplayName,
            string? daylightDisplayName,
            AdjustmentRule[]? adjustmentRules,
            bool disableDaylightSavingTime) =>
            throw UnsupportedCustomZone();

        public static TimeZoneInfo FromSerializedString(string source) =>
            throw new PlatformNotSupportedException(
                "Serialized timezone data is not supported.");

        public string ToSerializedString() =>
            throw new PlatformNotSupportedException(
                "Serialized timezone data is not supported.");

        public static bool TryConvertIanaIdToWindowsId(
            string ianaId,
            out string? windowsId)
        {
            ArgumentNullException.ThrowIfNull(ianaId);
            windowsId = null;
            return false;
        }

        public static bool TryConvertWindowsIdToIanaId(
            string windowsId,
            out string? ianaId) =>
            TryConvertWindowsIdToIanaId(windowsId, null, out ianaId);

        public static bool TryConvertWindowsIdToIanaId(
            string windowsId,
            string? region,
            out string? ianaId)
        {
            ArgumentNullException.ThrowIfNull(windowsId);
            ianaId = null;
            return false;
        }

        public static void ClearCachedData()
        {
            s_local = null;
            s_catalog = null;
        }

        public bool Equals(TimeZoneInfo? other) =>
            other != null &&
            string.Equals(_id, other._id, StringComparison.OrdinalIgnoreCase) &&
            HasSameRules(other);

        public override bool Equals(object? obj) => obj is TimeZoneInfo other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(_id);

        public override string ToString() => DisplayName;

        private static TimeZoneInfo CreateUtc()
        {
            var definition = new TimeZoneDefinition(
                UtcId,
                0,
                false,
                Array.Empty<TimeZoneTransition>());
            return new TimeZoneInfo(
                UtcId,
                definition,
                new TimeZoneTransitionResolver(),
                isUtc: true,
                isLocal: false,
                hasIanaId: true,
                Array.Empty<AdjustmentRule>());
        }

        private static TimeZoneInfo CreateLocal()
        {
            PlatformServices.EnsureLocalTimeReady();
            return (s_catalog ??= new TimeZoneCatalog()).CreateLocal();
        }

        private long GetUtcTicks(DateTime dateTime, bool throwOnInvalid)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                return dateTime.Ticks;
            }

            var localTicks = dateTime.Ticks;
            if (throwOnInvalid && IsInvalidTime(dateTime))
            {
                throw new ArgumentException(
                    "The supplied time is invalid in the source timezone.",
                    nameof(dateTime));
            }

            if (dateTime.Kind == DateTimeKind.Local && !_isLocal)
            {
                var localOffset = PlatformServices.LocalTimeOffsets.Resolve(
                    localTicks,
                    LocalTimeBasis.Local);
                return checked(localTicks - localOffset.Ticks);
            }

            var offset = _transitions.Resolve(
                _definition,
                localTicks,
                dateTime.IsAmbiguousDaylightSavingTime()
                    ? LocalTimeBasis.LocalAmbiguousDaylight
                    : LocalTimeBasis.Local);
            return localTicks - offset.Ticks;
        }

        private long GetLocalTicksForInspection(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Unspecified || _isLocal &&
                dateTime.Kind == DateTimeKind.Local)
            {
                return dateTime.Ticks;
            }

            var utcTicks = dateTime.Kind == DateTimeKind.Utc
                ? dateTime.Ticks
                : GetUtcTicks(dateTime, throwOnInvalid: false);
            var offset = _transitions.Resolve(
                _definition,
                utcTicks,
                LocalTimeBasis.Utc);
            return checked(utcTicks + offset.Ticks);
        }

        private bool IsInvalidLocalTicks(long localTicks)
        {
            return TimeZoneTransitionResolver.IsInvalidLocalTime(
                _definition,
                localTicks);
        }

        private bool TryGetAmbiguousOffsets(
            long localTicks,
            out TimeSpan first,
            out TimeSpan second)
        {
            if (TimeZoneTransitionResolver.TryGetAmbiguousOffsets(
                _definition,
                localTicks,
                out var firstSeconds,
                out var secondSeconds))
            {
                first = TimeSpan.FromSeconds(firstSeconds);
                second = TimeSpan.FromSeconds(secondSeconds);
                return true;
            }
            first = default;
            second = default;
            return false;
        }

        private static long AddSeconds(long unixSeconds, int offsetSeconds)
        {
            if (unixSeconds > 253402300799L)
            {
                return long.MaxValue;
            }
            if (unixSeconds < -62135596800L)
            {
                return long.MinValue;
            }

            var ticks = UnixEpochTicks + unixSeconds * TicksPerSecond;
            var offsetTicks = offsetSeconds * TicksPerSecond;
            return offsetTicks > 0 && ticks > long.MaxValue - offsetTicks
                ? long.MaxValue
                : offsetTicks < 0 && ticks < long.MinValue - offsetTicks
                    ? long.MinValue
                    : ticks + offsetTicks;
        }

        private static int ResolveBaseUtcOffsetSeconds(TimeZoneDefinition definition)
        {
            for (var index = definition.Transitions.Length - 1; index >= 0; index--)
            {
                var transition = definition.Transitions[index];
                if (!transition.IsDaylightSavingTime)
                {
                    return transition.OffsetSeconds;
                }
            }

            return definition.InitialOffsetSeconds;
        }

        private static bool HasDaylightTransitions(TimeZoneDefinition definition)
        {
            if (definition.InitialDaylightSavingTime)
            {
                return true;
            }
            foreach (var transition in definition.Transitions)
            {
                if (transition.IsDaylightSavingTime)
                {
                    return true;
                }
            }
            return false;
        }

        private AdjustmentRule[] GetOrCreateAdjustmentRules() =>
            _adjustmentRules ??= CreateAdjustmentRules(_definition);

        private static AdjustmentRule[] CreateAdjustmentRules(
            TimeZoneDefinition definition)
        {
            if (definition.Transitions.Length == 0)
            {
                return Array.Empty<AdjustmentRule>();
            }

            var rules = new List<AdjustmentRule>(definition.Transitions.Length + 1);
            var baseOffsetSeconds = NormalizeOffsetToMinute(
                ResolveBaseUtcOffsetSeconds(definition));
            var start = DateTime.MinValue;
            var periodOffsetSeconds = definition.InitialOffsetSeconds;
            var periodIsDaylight = definition.InitialDaylightSavingTime;
            foreach (var transition in definition.Transitions)
            {
                var transitionTicks = AddSeconds(transition.UnixSeconds, 0);
                if (transitionTicks <= DateTime.MinValue.Ticks)
                {
                    periodOffsetSeconds = transition.OffsetSeconds;
                    periodIsDaylight = transition.IsDaylightSavingTime;
                    continue;
                }
                if (transitionTicks > DateTime.MaxValue.Ticks)
                {
                    break;
                }

                var transitionInstant = new DateTime(transitionTicks, DateTimeKind.Utc);
                rules.Add(CreateTransitionPeriodRule(
                    start,
                    transitionInstant.AddTicks(-1),
                    baseOffsetSeconds,
                    periodOffsetSeconds,
                    periodIsDaylight));
                start = transitionInstant;
                periodOffsetSeconds = transition.OffsetSeconds;
                periodIsDaylight = transition.IsDaylightSavingTime;
            }

            rules.Add(CreateTransitionPeriodRule(
                start,
                DateTime.MaxValue,
                baseOffsetSeconds,
                periodOffsetSeconds,
                periodIsDaylight));
            return rules.ToArray();
        }

        private static AdjustmentRule CreateTransitionPeriodRule(
            DateTime start,
            DateTime end,
            int baseOffsetSeconds,
            int periodOffsetSeconds,
            bool isDaylight)
        {
            var transitionOffset = TimeSpan.FromSeconds(periodOffsetSeconds) -
                TimeSpan.FromSeconds(baseOffsetSeconds);
            if (transitionOffset.Ticks % TimeSpan.TicksPerMinute != 0)
            {
                transitionOffset = new TimeSpan(
                    transitionOffset.Hours,
                    transitionOffset.Minutes,
                    0);
            }

            var daylightDelta = isDaylight ? transitionOffset : TimeSpan.Zero;
            var baseUtcOffsetDelta = isDaylight ? TimeSpan.Zero : transitionOffset;
            var rule = AdjustmentRule.CreateAdjustmentRule(
                start,
                end,
                daylightDelta,
                isDaylight ? s_daylightRuleMarker : default,
                default,
                baseUtcOffsetDelta,
                noDaylightTransitions: true);
            return NormalizeAdjustmentRuleOffset(baseOffsetSeconds, rule);
        }

        private static AdjustmentRule NormalizeAdjustmentRuleOffset(
            int baseOffsetSeconds,
            AdjustmentRule rule)
        {
            var utcOffset = TimeSpan.FromSeconds(baseOffsetSeconds) +
                rule.BaseUtcOffsetDelta +
                (rule.HasDaylightSaving ? rule.DaylightDelta : TimeSpan.Zero);
            var maximum = TimeSpan.FromHours(14);
            var minimum = TimeSpan.FromHours(-14);
            var adjustment = utcOffset > maximum
                ? maximum - utcOffset
                : utcOffset < minimum
                    ? minimum - utcOffset
                    : TimeSpan.Zero;
            return adjustment == TimeSpan.Zero
                ? rule
                : AdjustmentRule.CreateAdjustmentRule(
                    rule.DateStart,
                    rule.DateEnd,
                    rule.DaylightDelta,
                    rule.DaylightTransitionStart,
                    rule.DaylightTransitionEnd,
                    rule.BaseUtcOffsetDelta + adjustment,
                    rule.NoDaylightTransitions);
        }

        private static int NormalizeOffsetToMinute(int offsetSeconds) =>
            offsetSeconds / 60 * 60;

        private static PlatformNotSupportedException UnsupportedCustomZone() =>
            new("Custom timezone definitions are not supported.");

        private sealed class TimeZoneCatalog
        {
            private readonly string? _localId;
            private readonly TimeZoneAsset? _asset;
            private readonly ITimeZoneDefinitionResolver? _definitions;
            private readonly ITimeZoneTransitionResolver _transitions =
                new TimeZoneTransitionResolver();
            private readonly Dictionary<string, TimeZoneInfo> _zones =
                new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);

            internal TimeZoneCatalog()
            {
                var snapshot = PlatformServices.TimeZoneCatalogSnapshot;
                _localId = snapshot.LocalId;
                if (_localId == null || _localId == UtcId || _localId == "Etc/UTC")
                {
                    return;
                }
                _asset = snapshot.Asset;
                _definitions = snapshot.Definitions;
            }

            internal TimeZoneInfo CreateLocal()
            {
                if (_localId == null || _localId == UtcId || _localId == "Etc/UTC")
                {
                    return new TimeZoneInfo(
                        UtcId,
                        s_utc._definition,
                        s_utc._transitions,
                        isUtc: false,
                        isLocal: true,
                        hasIanaId: true,
                        Array.Empty<AdjustmentRule>());
                }
                return Create(_localId, isLocal: true);
            }

            internal TimeZoneInfo Resolve(string id) =>
                _zones.TryGetValue(id, out var zone)
                    ? zone
                    : Create(id, isLocal: false);

            internal List<TimeZoneInfo> GetAll()
            {
                var result = new List<TimeZoneInfo> { Utc };
                if (_asset != null)
                {
                    foreach (var zone in _asset.Zones)
                    {
                        result.Add(Create(zone.Name, isLocal: false));
                    }
                }
                return result;
            }

            private TimeZoneInfo Create(string id, bool isLocal)
            {
                if (_definitions == null)
                {
                    throw new PlatformNotSupportedException(
                        "A non-UTC timezone asset is not configured.");
                }
                var definition = _definitions.Resolve(id);
                var zone = new TimeZoneInfo(
                    id,
                    definition,
                    _transitions,
                    isUtc: false,
                    isLocal,
                    hasIanaId: true,
                    null);
                _zones[id] = zone;
                return zone;
            }

        }
    }
}
