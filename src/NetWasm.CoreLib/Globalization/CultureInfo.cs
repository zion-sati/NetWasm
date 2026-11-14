// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptations: NetWasm runs upstream's invariant globalization mode only, so CultureData is
// the invariant stand-in and every named culture or LCID other than the invariant one throws
// CultureNotFoundException, as upstream does with PredefinedCulturesOnly. NetWasm runs one managed
// thread, so upstream's [ThreadStatic] current-culture fields are ordinary statics while the
// AsyncLocal flow is kept. Whenever the current culture changes, its NumberFormatInfo and
// DateTimeFormatInfo are mirrored into those types' CurrentInfo, so formatting with a null provider
// does not reach CultureInfo. Every invariant culture shares the invariant CompareInfo. The user
// default culture is always the invariant culture, and ClearCachedData has no host data to reset.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace System.Globalization
{
    /// <summary>
    /// This class represents the software preferences of a particular culture
    /// or community. It includes information such as the language, writing
    /// system and a calendar used by the culture as well as methods for
    /// common operations such as printing dates and sorting strings.
    /// </summary>
    public partial class CultureInfo : IFormatProvider, ICloneable
    {
        // We use an RFC4646 type string to construct CultureInfo.
        // This string is stored in _name and is authoritative.
        // We use the _cultureData to get the data for our object

        private bool _isReadOnly;
        private CompareInfo? _compareInfo;
        private TextInfo? _textInfo;
        internal NumberFormatInfo? _numInfo;
        internal DateTimeFormatInfo? _dateTimeInfo;
        private Calendar? _calendar;
        //
        // The CultureData instance that we are going to read data from.
        //
        internal CultureData _cultureData;

        internal bool _isInherited;

        private CultureInfo? _consoleFallbackCulture;

        // This has a de-DE, de-DE_phoneb or fj-FJ style name
        internal string _name;

        // This will hold the non sorting name to be returned from CultureInfo.Name property.
        private string? _nonSortName;

        // This will hold the sorting name to be returned from CultureInfo.SortName property.
        private string? _sortName;

        // Get the current user default culture. This one is almost always used, so we create it by default.
        private static CultureInfo? s_userDefaultCulture;

        // The culture used in the user interface. This is mostly used to load correct localized resources.
        private static CultureInfo? s_userDefaultUICulture;

        // The Invariant culture;
        private static readonly CultureInfo s_InvariantCultureInfo = new CultureInfo(CultureData.Invariant, isReadOnly: true);

        // These are defaults that we use if a thread has not opted into having an explicit culture
        private static volatile CultureInfo? s_DefaultThreadCurrentUICulture;
        private static volatile CultureInfo? s_DefaultThreadCurrentCulture;

        private static CultureInfo? s_currentThreadCulture;
        private static CultureInfo? s_currentThreadUICulture;

        private static AsyncLocal<CultureInfo>? s_asyncLocalCurrentCulture;
        private static AsyncLocal<CultureInfo>? s_asyncLocalCurrentUICulture;

        private static void AsyncLocalSetCurrentCulture(AsyncLocalValueChangedArgs<CultureInfo> args)
        {
            s_currentThreadCulture = args.CurrentValue;
            OnCurrentCultureChanged();
        }

        private static void AsyncLocalSetCurrentUICulture(AsyncLocalValueChangedArgs<CultureInfo> args)
        {
            s_currentThreadUICulture = args.CurrentValue;
        }

        private static Dictionary<string, CultureInfo>? s_cachedCulturesByName;
        private static Dictionary<int, CultureInfo>? s_cachedCulturesByLcid;

        // The parent culture.
        private CultureInfo? _parent;

        // LOCALE constants of interest to us internally and privately for LCID functions
        // (ie: avoid using these and use names if possible)
        internal const int LOCALE_NEUTRAL = 0x0000;
        private const int LOCALE_USER_DEFAULT = 0x0400;
        private const int LOCALE_SYSTEM_DEFAULT = 0x0800;
        internal const int LOCALE_CUSTOM_UNSPECIFIED = 0x1000;
        internal const int LOCALE_CUSTOM_DEFAULT = 0x0c00;
        internal const int LOCALE_INVARIANT = 0x007F;

        private static CultureInfo InitializeUserDefaultCulture()
        {
            Interlocked.CompareExchange(ref s_userDefaultCulture, GetUserDefaultCulture(), null);
            return s_userDefaultCulture;
        }

        private static CultureInfo InitializeUserDefaultUICulture()
        {
            Interlocked.CompareExchange(ref s_userDefaultUICulture, GetUserDefaultUICulture(), null);
            return s_userDefaultUICulture;
        }

        // Invariant mode's user default culture, as upstream's CultureInfo.Unix returns it.
        internal static CultureInfo GetUserDefaultCulture() => InvariantCulture;

        private static CultureInfo GetUserDefaultUICulture() => InitializeUserDefaultCulture();

        private static string GetCultureNotSupportedExceptionMessage() => SR.Argument_CultureNotSupportedInInvariantMode;

        public CultureInfo(string name) : this(name, true)
        {
        }

        public CultureInfo(string name, bool useUserOverride)
        {
            ArgumentNullException.ThrowIfNull(name);

            // Get our data providing record
            _cultureData = CultureData.GetCultureData(name, useUserOverride) ??
                throw new CultureNotFoundException(nameof(name), name, GetCultureNotSupportedExceptionMessage());
            _name = _cultureData.CultureName;
            _isInherited = GetType() != typeof(CultureInfo);
        }

        private CultureInfo(CultureData cultureData, bool isReadOnly = false)
        {
            Debug.Assert(cultureData != null);
            _cultureData = cultureData;
            _name = cultureData.CultureName;
            _isReadOnly = isReadOnly;
        }

        private static CultureInfo? CreateCultureInfoNoThrow(string name, bool useUserOverride)
        {
            Debug.Assert(name != null);
            CultureData? cultureData = CultureData.GetCultureData(name, useUserOverride);
            if (cultureData == null)
            {
                return null;
            }

            return new CultureInfo(cultureData);
        }

        public CultureInfo(int culture) : this(culture, true)
        {
        }

        public CultureInfo(int culture, bool useUserOverride)
        {
            // We don't check for other invalid LCIDS here...
            ArgumentOutOfRangeException.ThrowIfNegative(culture);

            if (culture is LOCALE_CUSTOM_DEFAULT or LOCALE_SYSTEM_DEFAULT or LOCALE_NEUTRAL or LOCALE_USER_DEFAULT or LOCALE_CUSTOM_UNSPECIFIED)
            {
                // Can't support unknown custom cultures and we do not support neutral or
                throw new CultureNotFoundException(nameof(culture), culture, SR.Argument_CultureNotSupported);
            }

            // Now see if this LCID is supported in the system default CultureData table.
            _cultureData = CultureData.GetCultureData(culture, useUserOverride);

            _isInherited = GetType() != typeof(CultureInfo);
            _name = _cultureData.CultureName;
        }

        /// <summary>
        /// Constructor called by SQL Server's special munged culture - creates a culture with
        /// a TextInfo and CompareInfo that come from a supplied alternate source. This object
        /// is ALWAYS read-only.
        /// </summary>
        internal CultureInfo(string cultureName, string textAndCompareCultureName)
        {
            ArgumentNullException.ThrowIfNull(textAndCompareCultureName);

            CultureData? cultureData = CultureData.GetCultureData(cultureName, false) ??
                throw new CultureNotFoundException(nameof(cultureName), cultureName, GetCultureNotSupportedExceptionMessage());

            _cultureData = cultureData;

            _name = _cultureData.CultureName;

            CultureInfo altCulture = GetCultureInfo(textAndCompareCultureName);
            _compareInfo = altCulture.CompareInfo;
            _textInfo = altCulture.TextInfo;
        }

        /// <summary>
        /// Return a specific culture. A tad irrelevant now since we always
        /// return valid data for neutral locales.
        /// </summary>
        public static CultureInfo CreateSpecificCulture(string name)
        {
            CultureInfo? culture;

            try
            {
                culture = new CultureInfo(name);
            }
            catch (ArgumentException)
            {
                // When CultureInfo throws this exception, it may be because someone passed the form
                // like "az-az" because it came out of an http accept lang. We should try a little
                // parsing to perhaps fall back to "az" here and use *it* to create the neutral.
                int idx = name.IndexOf('-');
                if (idx >= 0)
                {
                    try
                    {
                        culture = new CultureInfo(name.Substring(0, idx));
                    }
                    catch (ArgumentException)
                    {
                        // throw the original exception so the name in the string will be right
                        throw;
                    }
                }
                else
                {
                    // nothing to save here; throw the original exception
                    throw;
                }
            }

            // In the most common case, they've given us a specific culture, so we'll just return that.
            if (!culture.IsNeutralCulture)
            {
                return culture;
            }

            return new CultureInfo(culture._cultureData.SpecificCultureName);
        }

        internal static bool VerifyCultureName(string cultureName, bool throwException)
        {
            // This function is used by ResourceManager.GetResourceFileName().
            // ResourceManager searches for resource using CultureInfo.Name,
            // so we should check against CultureInfo.Name.
            for (int i = 0; i < cultureName.Length; i++)
            {
                char c = cultureName[i];
                // TODO: Names can only be RFC4646 names (ie: a-zA-Z0-9) while this allows any unicode letter/digit
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    continue;
                }
                if (throwException)
                {
                    throw new ArgumentException(SR.Format(SR.Argument_InvalidResourceCultureName, cultureName));
                }
                return false;
            }
            return true;
        }

        internal static bool VerifyCultureName(CultureInfo culture, bool throwException)
        {
            // If we have an instance of one of our CultureInfos, the user can't have changed the
            // name and we know that all names are valid in files.
            if (!culture._isInherited)
            {
                return true;
            }

            return VerifyCultureName(culture.Name, throwException);
        }

        /// <summary>
        /// This instance provides methods based on the current user settings.
        /// These settings are volatile and may change over the lifetime of the
        /// thread.
        /// </summary>
        public static CultureInfo CurrentCulture
        {
            get
            {
                return s_currentThreadCulture ??
                    s_DefaultThreadCurrentCulture ??
                    s_userDefaultCulture ??
                    InitializeUserDefaultCulture();
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (s_asyncLocalCurrentCulture == null)
                {
                    Interlocked.CompareExchange(ref s_asyncLocalCurrentCulture, new AsyncLocal<CultureInfo>(AsyncLocalSetCurrentCulture), null);
                }
                s_asyncLocalCurrentCulture.Value = value;
            }
        }

        public static CultureInfo CurrentUICulture
        {
            get
            {
                return s_currentThreadUICulture ??
                    s_DefaultThreadCurrentUICulture ??
                    UserDefaultUICulture;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyCultureName(value, true);

                if (s_asyncLocalCurrentUICulture == null)
                {
                    Interlocked.CompareExchange(ref s_asyncLocalCurrentUICulture, new AsyncLocal<CultureInfo>(AsyncLocalSetCurrentUICulture), null);
                }

                // this one will set s_currentThreadUICulture too
                s_asyncLocalCurrentUICulture.Value = value;
            }
        }

        internal static CultureInfo UserDefaultUICulture => s_userDefaultUICulture ?? InitializeUserDefaultUICulture();

        public static CultureInfo InstalledUICulture => s_userDefaultCulture ?? InitializeUserDefaultCulture();

        public static CultureInfo? DefaultThreadCurrentCulture
        {
            get => s_DefaultThreadCurrentCulture;
            set
            {
                // If you add pre-conditions to this method, check to see if you also need to
                // add them to Thread.CurrentCulture.set.
                s_DefaultThreadCurrentCulture = value;
                OnCurrentCultureChanged();
            }
        }

        public static CultureInfo? DefaultThreadCurrentUICulture
        {
            get => s_DefaultThreadCurrentUICulture;
            set
            {
                // If they're trying to use a Culture with a name that we can't use in resource lookup,
                // don't even let them set it on the thread.

                // If you add more pre-conditions to this method, check to see if you also need to
                // add them to Thread.CurrentUICulture.set.

                if (value != null)
                {
                    VerifyCultureName(value, true);
                }

                s_DefaultThreadCurrentUICulture = value;
            }
        }

        // NetWasm: mirrors the current culture and its formats so that formatting with a null provider reads
        // NumberFormatInfo.CurrentInfo and DateTimeFormatInfo.CurrentInfo, and capturing the current culture
        // reads CurrentCultureMirror, without reaching CultureInfo.
        private static void OnCurrentCultureChanged()
        {
            CultureInfo culture = CurrentCulture;
            if (ReferenceEquals(culture, s_InvariantCultureInfo))
            {
                CurrentCultureMirror.Culture = null;
                CurrentNumberFormatInfo.Value = null;
                DateTimeFormatInfo.s_currentInfo = null;
                return;
            }

            CurrentCultureMirror.Culture = culture;
            CurrentNumberFormatInfo.Value = (NumberFormatInfo)culture.GetFormat(typeof(NumberFormatInfo))!;
            DateTimeFormatInfo.s_currentInfo = (DateTimeFormatInfo)culture.GetFormat(typeof(DateTimeFormatInfo))!;
        }

        /// <summary>
        /// This instance provides methods, for example for casing and sorting,
        /// that are independent of the system and current user settings.  It
        /// should be used only by processes such as some system services that
        /// require such invariant results (eg. file systems).  In general,
        /// the results are not linguistically correct and do not match any
        /// culture info.
        /// </summary>
        public static CultureInfo InvariantCulture
        {
            get
            {
                Debug.Assert(s_InvariantCultureInfo != null, "[CultureInfo.InvariantCulture] s_InvariantCultureInfo is null");
                return s_InvariantCultureInfo;
            }
        }

        /// <summary>
        /// Return the parent CultureInfo for the current instance.
        /// </summary>
        public virtual CultureInfo Parent
        {
            get
            {
                if (_parent == null)
                {
                    CultureInfo culture;
                    string parentName = _cultureData.ParentName;

                    if (string.IsNullOrEmpty(parentName))
                    {
                        culture = InvariantCulture;
                    }
                    else
                    {
                        culture = CreateCultureInfoNoThrow(parentName, _cultureData.UseUserOverride) ??
                            // For whatever reason our IPARENT or SPARENT wasn't correct, so use invariant
                            // We can't allow ourselves to fail.  In case of custom cultures the parent of the
                            // current custom culture isn't installed.
                            InvariantCulture;
                    }

                    Interlocked.CompareExchange<CultureInfo?>(ref _parent, culture, null);
                }
                return _parent!;
            }
        }

        public virtual int LCID => _cultureData.LCID;

        public virtual int KeyboardLayoutId => _cultureData.KeyboardLayoutId;

        public static CultureInfo[] GetCultures(CultureTypes types)
        {
            // internally we treat UserCustomCultures as Supplementals but v2
            // treats as Supplementals and Replacements
            if ((types & CultureTypes.UserCustomCulture) == CultureTypes.UserCustomCulture)
            {
                types |= CultureTypes.ReplacementCultures;
            }
            return CultureData.GetCultures(types);
        }

        /// <summary>
        /// Returns the full name of the CultureInfo. The name is in format like
        /// "en-US" This version does NOT include sort information in the name.
        /// </summary>
        public virtual string Name => _nonSortName ??= (_cultureData.Name ?? string.Empty);

        // This one has the sort information (ie: de-DE_phoneb)
        internal string SortName => _sortName ??= _cultureData.SortName;

        // Unix doesn't use this
        internal string? InteropName => _cultureData.InteropName;

        public string IetfLanguageTag =>
                // special case the compatibility cultures
                Name switch
                {
                    "zh-CHT" => "zh-Hant",
                    "zh-CHS" => "zh-Hans",
                    _ => Name,
                };

        /// <summary>
        /// Returns the full name of the CultureInfo in the localized language.
        /// For example, if the localized language of the runtime is Spanish and the CultureInfo is
        /// US English, "Ingles (Estados Unidos)" will be returned.
        /// </summary>
        public virtual string DisplayName
        {
            get
            {
                Debug.Assert(_name != null, "[CultureInfo.DisplayName] Always expect _name to be set");
                return _cultureData.DisplayName;
            }
        }

        /// <summary>
        /// Returns the full name of the CultureInfo in the native language.
        /// For example, if the CultureInfo is US English, "English
        /// (United States)" will be returned.
        /// </summary>
        public virtual string NativeName => _cultureData.NativeName;

        /// <summary>
        /// Returns the full name of the CultureInfo in English.
        /// For example, if the CultureInfo is US English, "English
        /// (United States)" will be returned.
        /// </summary>
        public virtual string EnglishName => _cultureData.EnglishName;

        /// <summary>
        /// ie: en
        /// </summary>
        public virtual string TwoLetterISOLanguageName => _cultureData.TwoLetterISOLanguageName;

        /// <summary>
        /// ie: eng
        /// </summary>
        public virtual string ThreeLetterISOLanguageName => _cultureData.ThreeLetterISOLanguageName;

        /// <summary>
        /// Returns the 3 letter windows language name for the current instance.  eg: "ENU"
        /// The ISO names are much preferred
        /// </summary>
        public virtual string ThreeLetterWindowsLanguageName => _cultureData.ThreeLetterWindowsLanguageName;

        /// <summary>
        /// Gets the CompareInfo for this culture.
        /// </summary>
        public virtual CompareInfo CompareInfo => _compareInfo ??= CompareInfo.Invariant;

        /// <summary>
        /// Gets the TextInfo for this culture.
        /// </summary>
        public virtual TextInfo TextInfo
        {
            get
            {
                if (_textInfo == null)
                {
                    // Make a new textInfo
                    TextInfo tempTextInfo = new TextInfo(_cultureData);
                    tempTextInfo.SetReadOnlyState(_isReadOnly);
                    _textInfo = tempTextInfo;
                }
                return _textInfo;
            }
        }

        public override bool Equals([NotNullWhen(true)] object? value)
        {
            if (ReferenceEquals(this, value))
            {
                return true;
            }

            if (value is CultureInfo that)
            {
                // using CompareInfo to verify the data passed through the constructor
                // CultureInfo(String cultureName, String textAndCompareCultureName)
                return Name.Equals(that.Name) && CompareInfo.Equals(that.CompareInfo);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode() + CompareInfo.GetHashCode();
        }

        /// <summary>
        /// Implements object.ToString(). Returns the name of the CultureInfo,
        /// eg. "de-DE_phoneb", "en-US", or "fj-FJ".
        /// </summary>
        public override string ToString() => _name;

        public virtual object? GetFormat(Type? formatType)
        {
            if (formatType == typeof(NumberFormatInfo))
            {
                return NumberFormat;
            }
            if (formatType == typeof(DateTimeFormatInfo))
            {
                return DateTimeFormat;
            }

            return null;
        }

        public virtual bool IsNeutralCulture => _cultureData.IsNeutralCulture;

        public CultureTypes CultureTypes
        {
            get
            {
                CultureTypes types = _cultureData.IsNeutralCulture ?
                    CultureTypes.NeutralCultures :
                    CultureTypes.SpecificCultures;

                if (CultureData.IsWin32Installed)
                {
                    types |= CultureTypes.InstalledWin32Cultures;
                }

                if (_cultureData.IsSupplementalCustomCulture)
                {
                    types |= CultureTypes.UserCustomCulture;
                }

                if (_cultureData.IsReplacementCulture)
                {
                    types |= CultureTypes.ReplacementCultures;
                }

                return types;
            }
        }

        public virtual NumberFormatInfo NumberFormat
        {
            get
            {
                if (_numInfo == null)
                {
                    // NetWasm: the read-only invariant culture shares NumberFormatInfo.InvariantInfo,
                    // which upstream obtains from this property.
                    NumberFormatInfo temp = ReferenceEquals(this, s_InvariantCultureInfo) ?
                        NumberFormatInfo.InvariantInfo :
                        new NumberFormatInfo(_cultureData) { _isReadOnly = _isReadOnly };
                    Interlocked.CompareExchange(ref _numInfo, temp, null);
                }
                return _numInfo!;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _numInfo = value;
                if (ReferenceEquals(this, CurrentCulture))
                {
                    CurrentNumberFormatInfo.Value = value;
                }
            }
        }

        /// <summary>
        /// Create a DateTimeFormatInfo, and fill in the properties according to
        /// the CultureID.
        /// </summary>
        public virtual DateTimeFormatInfo DateTimeFormat
        {
            get
            {
                if (_dateTimeInfo == null)
                {
                    // NetWasm: the read-only invariant culture shares DateTimeFormatInfo.InvariantInfo.
                    DateTimeFormatInfo temp = ReferenceEquals(this, s_InvariantCultureInfo) ?
                        DateTimeFormatInfo.InvariantInfo :
                        new DateTimeFormatInfo(_cultureData, this.Calendar) { _isReadOnly = _isReadOnly };
                    Interlocked.CompareExchange(ref _dateTimeInfo, temp, null);
                }
                return _dateTimeInfo!;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);

                VerifyWritable();
                _dateTimeInfo = value;
                if (ReferenceEquals(this, CurrentCulture))
                {
                    DateTimeFormatInfo.s_currentInfo = value;
                }
            }
        }

        public void ClearCachedData()
        {
            // reset the default culture values
            s_userDefaultCulture = GetUserDefaultCulture();
            s_userDefaultUICulture = GetUserDefaultUICulture();

            RegionInfo.s_currentRegionInfo = null;
            s_cachedCulturesByLcid = null;
            s_cachedCulturesByName = null;

            CultureData.ClearCachedData();
        }

        /// <summary>
        /// Return/set the default calendar used by this culture.
        /// This value can be overridden by regional option if this is a current culture.
        /// </summary>
        public virtual Calendar Calendar
        {
            get
            {
                if (_calendar == null)
                {
                    // Get the default calendar for this culture.  Note that the value can be
                    // from registry if this is a user default culture.
                    Calendar newObj = _cultureData.DefaultCalendar;

                    Interlocked.MemoryBarrier();
                    newObj.SetReadOnlyState(_isReadOnly);
                    _calendar = newObj;
                }
                return _calendar;
            }
        }

        /// <summary>
        /// Return an array of the optional calendar for this culture.
        /// </summary>
        public virtual Calendar[] OptionalCalendars
        {
            get
            {
                // This property always returns a new copy of the calendar array.
                return new[] { new GregorianCalendar() };
            }
        }

        public bool UseUserOverride => _cultureData.UseUserOverride;

        public CultureInfo GetConsoleFallbackUICulture()
        {
            CultureInfo? temp = _consoleFallbackCulture;
            if (temp == null)
            {
                temp = CreateSpecificCulture(_cultureData.SCONSOLEFALLBACKNAME);
                temp._isReadOnly = true;
                _consoleFallbackCulture = temp;
            }
            return temp;
        }

        public virtual object Clone()
        {
            CultureInfo ci = (CultureInfo)MemberwiseClone();
            ci._isReadOnly = false;

            // If this is exactly our type, we can make certain optimizations so that we don't allocate NumberFormatInfo or DTFI unless
            // they've already been allocated.  If this is a derived type, we'll take a more generic codepath.
            if (!_isInherited)
            {
                if (_dateTimeInfo != null)
                {
                    ci._dateTimeInfo = (DateTimeFormatInfo)_dateTimeInfo.Clone();
                }
                if (_numInfo != null)
                {
                    ci._numInfo = (NumberFormatInfo)_numInfo.Clone();
                }
            }
            else
            {
                ci.DateTimeFormat = (DateTimeFormatInfo)this.DateTimeFormat.Clone();
                ci.NumberFormat = (NumberFormatInfo)this.NumberFormat.Clone();
            }

            if (_textInfo != null)
            {
                ci._textInfo = (TextInfo)_textInfo.Clone();
            }

            if (_dateTimeInfo != null && _dateTimeInfo.Calendar == _calendar)
            {
                // Usually when we access CultureInfo.DateTimeFormat first time, we create the DateTimeFormatInfo object
                // using CultureInfo.Calendar. i.e. CultureInfo.DateTimeInfo.Calendar == CultureInfo.calendar.
                // When cloning CultureInfo, if we know it's still the case that CultureInfo.DateTimeInfo.Calendar == CultureInfo.calendar
                // then we can keep the same behavior for the cloned object and no need to create another calendar object.
                ci._calendar = ci.DateTimeFormat.Calendar;
            }
            else if (_calendar != null)
            {
                ci._calendar = (Calendar)_calendar.Clone();
            }

            return ci;
        }

        public static CultureInfo ReadOnly(CultureInfo ci)
        {
            ArgumentNullException.ThrowIfNull(ci);

            if (ci.IsReadOnly)
            {
                return ci;
            }
            CultureInfo newInfo = (CultureInfo)(ci.MemberwiseClone());

            if (!ci.IsNeutralCulture)
            {
                // If this is exactly our type, we can make certain optimizations so that we don't allocate NumberFormatInfo or DTFI unless
                // they've already been allocated.  If this is a derived type, we'll take a more generic codepath.
                if (!ci._isInherited)
                {
                    if (ci._dateTimeInfo != null)
                    {
                        newInfo._dateTimeInfo = DateTimeFormatInfo.ReadOnly(ci._dateTimeInfo);
                    }
                    if (ci._numInfo != null)
                    {
                        newInfo._numInfo = NumberFormatInfo.ReadOnly(ci._numInfo);
                    }
                }
                else
                {
                    newInfo.DateTimeFormat = DateTimeFormatInfo.ReadOnly(ci.DateTimeFormat);
                    newInfo.NumberFormat = NumberFormatInfo.ReadOnly(ci.NumberFormat);
                }
            }

            if (ci._textInfo != null)
            {
                newInfo._textInfo = TextInfo.ReadOnly(ci._textInfo);
            }

            if (ci._calendar != null)
            {
                newInfo._calendar = Calendar.ReadOnly(ci._calendar);
            }

            // Don't set the read-only flag too early.
            // We should set the read-only flag here.  Otherwise, info.DateTimeFormat will not be able to set.
            newInfo._isReadOnly = true;

            return newInfo;
        }

        public bool IsReadOnly => _isReadOnly;

        private void VerifyWritable()
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException(SR.InvalidOperation_ReadOnly);
            }
        }

        /// <summary>
        /// For resource lookup, we consider a culture the invariant culture by name equality.
        /// We perform this check frequently during resource lookup, so adding a property for
        /// improved readability.
        /// </summary>
        internal bool HasInvariantCultureName => Name == InvariantCulture.Name;

        /// <summary>
        /// Gets a cached copy of the specified culture from an internal
        /// hashtable (or creates it if not found). (LCID version)
        /// </summary>
        public static CultureInfo GetCultureInfo(int culture)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(culture);

            Dictionary<int, CultureInfo> lcidTable = CachedCulturesByLcid;
            CultureInfo? result;

            lock (lcidTable)
            {
                if (lcidTable.TryGetValue(culture, out result))
                {
                    return result;
                }
            }

            try
            {
                result = new CultureInfo(culture, useUserOverride: false) { _isReadOnly = true };
            }
            catch (ArgumentException)
            {
                throw new CultureNotFoundException(nameof(culture), culture, GetCultureNotSupportedExceptionMessage());
            }

            lock (lcidTable)
            {
                lcidTable[culture] = result;
            }

            return result;
        }

        /// <summary>
        /// Gets a cached copy of the specified culture from an internal
        /// hashtable (or creates it if not found). (Named version)
        /// </summary>
        public static CultureInfo GetCultureInfo(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            name = CultureData.AnsiToLower(name);
            Dictionary<string, CultureInfo> nameTable = CachedCulturesByName;
            CultureInfo? result;

            lock (nameTable)
            {
                if (nameTable.TryGetValue(name, out result))
                {
                    return result;
                }
            }

            result = CreateCultureInfoNoThrow(name, useUserOverride: false) ??
                throw new CultureNotFoundException(nameof(name), name, GetCultureNotSupportedExceptionMessage());
            result._isReadOnly = true;

            // Remember our name as constructed.  Do NOT use alternate sort name versions because
            // we have internal state representing the sort (so someone would get the wrong cached version).
            name = CultureData.AnsiToLower(result._name);

            lock (nameTable)
            {
                // add only if it wasn't already added
                nameTable.TryAdd(name, result);
            }

            return result;
        }

        /// <summary>
        /// Gets a cached copy of the specified culture from an internal
        /// hashtable (or creates it if not found).
        /// </summary>
        public static CultureInfo GetCultureInfo(string name, string altName)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(altName);

            name = CultureData.AnsiToLower(name);
            altName = CultureData.AnsiToLower(altName);
            string nameAndAltName = name + "\xfffd" + altName;
            Dictionary<string, CultureInfo> nameTable = CachedCulturesByName;
            CultureInfo? result;

            lock (nameTable)
            {
                if (nameTable.TryGetValue(nameAndAltName, out result))
                {
                    return result;
                }
            }

            try
            {
                result = new CultureInfo(name, altName) { _isReadOnly = true };
                result.TextInfo.SetReadOnlyState(readOnly: true); // TextInfo object is already created; we need to set it as read only.
            }
            catch (ArgumentException)
            {
#pragma warning disable CA2208 // Instantiate argument exceptions correctly, combination of arguments used
                throw new CultureNotFoundException("name/altName", SR.Format(SR.Argument_OneOfCulturesNotSupported, name, altName));
#pragma warning restore CA2208
            }

            lock (nameTable)
            {
                nameTable[nameAndAltName] = result;
            }

            return result;
        }

        private static Dictionary<string, CultureInfo> CachedCulturesByName
        {
            get
            {
                Dictionary<string, CultureInfo>? cache = s_cachedCulturesByName;
                if (cache is null)
                {
                    cache = new Dictionary<string, CultureInfo>();
                    cache = Interlocked.CompareExchange(ref s_cachedCulturesByName, cache, null) ?? cache;
                }

                return cache;
            }
        }

        private static Dictionary<int, CultureInfo> CachedCulturesByLcid
        {
            get
            {
                Dictionary<int, CultureInfo>? cache = s_cachedCulturesByLcid;
                if (cache is null)
                {
                    cache = new Dictionary<int, CultureInfo>();
                    cache = Interlocked.CompareExchange(ref s_cachedCulturesByLcid, cache, null) ?? cache;
                }

                return cache;
            }
        }

        public static CultureInfo GetCultureInfoByIetfLanguageTag(string name)
        {
            // Disallow old zh-CHT/zh-CHS names
            if (name is "zh-CHT" or "zh-CHS")
            {
                throw new CultureNotFoundException(nameof(name), SR.Format(SR.Argument_CultureIetfNotSupported, name));
            }

            CultureInfo ci = GetCultureInfo(name);

            // Disallow alt sorts and es-es_TS
            if (ci.LCID > 0xffff || ci.LCID == 0x040a)
            {
                throw new CultureNotFoundException(nameof(name), SR.Format(SR.Argument_CultureIetfNotSupported, name));
            }

            return ci;
        }
    }
}
