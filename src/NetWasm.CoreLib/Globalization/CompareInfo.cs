// Derived from dotnet/runtime CompareInfo invariant-mode paths at
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm runs upstream's invariant globalization mode only: every culture shares this invariant
// CompareInfo, linguistic options map to ordinal or ordinal-ignore-case operations as upstream's
// invariant mode does, and sort keys are upstream's invariant big-endian UTF-16 keys. The
// IsPrefix/IsSuffix/IndexOf/LastIndexOf overloads keep upstream's argument validation; NetWasm's
// span search helpers replace upstream's internal Ordinal and TryGetSpan helpers.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace System.Globalization;

[Serializable]
public sealed partial class CompareInfo
{
    internal static readonly CompareInfo Invariant = new();

    private const CompareOptions ValidCompareOptions = CompareOptions.IgnoreCase |
        CompareOptions.IgnoreSymbols | CompareOptions.IgnoreNonSpace |
        CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType |
        CompareOptions.StringSort | CompareOptions.NumericOrdering;

    // Mask used to check if IndexOf()/LastIndexOf()/IsPrefix()/IsPostfix() has the right flags.
    private const CompareOptions ValidIndexMaskOffFlags =
        ~(CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols | CompareOptions.IgnoreNonSpace |
          CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType);

    private SortVersion? m_SortVersion;

    internal CompareInfo() { }

    // Upstream's CompareInfo.Nls.cs handle, which only NLS's casing and sorting take; NetWasm runs the
    // invariant globalization mode only.
    internal static IntPtr NlsGetSortHandle(string cultureName) => throw new PlatformNotSupportedException();

    public string Name => string.Empty;

    public static CompareInfo GetCompareInfo(int culture)
    {
        // CultureData.IsCustomCultureId
        if (culture is CultureInfo.LOCALE_CUSTOM_DEFAULT or CultureInfo.LOCALE_CUSTOM_UNSPECIFIED)
        {
            throw new ArgumentException(SR.Argument_CustomCultureCannotBePassedByNumber, nameof(culture));
        }

        return CultureInfo.GetCultureInfo(culture).CompareInfo;
    }

    public static CompareInfo GetCompareInfo(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CultureInfo.GetCultureInfo(name).CompareInfo;
    }

    public static bool IsSortable(char ch)
    {
        return IsSortable(new ReadOnlySpan<char>(in ch));
    }

    public static bool IsSortable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return IsSortable(text.AsSpan());
    }

    private static bool IsSortable(ReadOnlySpan<char> text)
    {
        // all chars are sortable in invariant mode
        return text.Length != 0;
    }

    public int Compare(string? string1, string? string2) => Compare(string1, string2, CompareOptions.None);

    public int Compare(string? string1, string? string2, CompareOptions options)
    {
        var comparison = InvariantComparison(options);
        return string.Compare(string1, string2, comparison);
    }

    public int Compare(string? string1, int offset1, int length1, string? string2, int offset2, int length2) =>
        Compare(string1, offset1, length1, string2, offset2, length2, CompareOptions.None);

    public int Compare(string? string1, int offset1, string? string2, int offset2, CompareOptions options) =>
        Compare(string1, offset1, string1 is null ? 0 : string1.Length - offset1,
            string2, offset2, string2 is null ? 0 : string2.Length - offset2, options);

    public int Compare(string? string1, int offset1, string? string2, int offset2) =>
        Compare(string1, offset1, string2, offset2, CompareOptions.None);

    public int Compare(string? string1, int offset1, int length1, string? string2, int offset2, int length2, CompareOptions options)
    {
        // Preserve upstream region validation order before checking comparison options.
        ArgumentOutOfRangeException.ThrowIfNegative(length1);
        ArgumentOutOfRangeException.ThrowIfNegative(length2);
        ArgumentOutOfRangeException.ThrowIfNegative(offset1);
        ArgumentOutOfRangeException.ThrowIfNegative(offset2);
        if (offset1 > (string1 is null ? 0 : string1.Length) - length1)
            throw new ArgumentOutOfRangeException(nameof(string1));
        if (offset2 > (string2 is null ? 0 : string2.Length) - length2)
            throw new ArgumentOutOfRangeException(nameof(string2));
        var comparison = InvariantComparison(options);
        if (string1 is null || string2 is null)
            return ReferenceEquals(string1, string2) ? 0 : string1 is null ? -1 : 1;
        return string1.AsSpan(offset1, length1).CompareTo(string2.AsSpan(offset2, length2), comparison);
    }

    public int Compare(ReadOnlySpan<char> string1, ReadOnlySpan<char> string2, CompareOptions options = CompareOptions.None) =>
        string1.CompareTo(string2, InvariantComparison(options));

    public bool IsPrefix(string source, string prefix, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(prefix);

        // The empty string is trivially a prefix of every other string. For compat with
        // earlier versions of the Framework we'll early-exit here before validating the
        // 'options' argument.
        if (prefix.Length == 0)
        {
            return true;
        }

        return source.AsSpan().StartsWith(prefix.AsSpan(), IndexComparison(options));
    }

    // The empty span is trivially a prefix, before the options are validated, as for strings.
    public bool IsPrefix(ReadOnlySpan<char> source, ReadOnlySpan<char> prefix, CompareOptions options = CompareOptions.None) =>
        prefix.IsEmpty || source.StartsWith(prefix, IndexComparison(options));

    public bool IsSuffix(ReadOnlySpan<char> source, ReadOnlySpan<char> suffix, CompareOptions options = CompareOptions.None) =>
        suffix.IsEmpty || source.EndsWith(suffix, IndexComparison(options));

    public bool IsPrefix(string source, string prefix)
    {
        return IsPrefix(source, prefix, CompareOptions.None);
    }

    public bool IsSuffix(string source, string suffix, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(suffix);

        // The empty string is trivially a suffix of every other string. For compat with
        // earlier versions of the Framework we'll early-exit here before validating the
        // 'options' argument.
        if (suffix.Length == 0)
        {
            return true;
        }

        return source.AsSpan().EndsWith(suffix.AsSpan(), IndexComparison(options));
    }

    public bool IsSuffix(string source, string suffix)
    {
        return IsSuffix(source, suffix, CompareOptions.None);
    }

    public int IndexOf(string source, char value)
    {
        return IndexOf(source, value, CompareOptions.None);
    }

    public int IndexOf(string source, string value)
    {
        return IndexOf(source, value, CompareOptions.None);
    }

    public int IndexOf(string source, char value, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        return IndexOf(source.AsSpan(), new ReadOnlySpan<char>(in value), options);
    }

    public int IndexOf(string source, string value, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(value);

        return IndexOf(source.AsSpan(), value.AsSpan(), options);
    }

    public int IndexOf(string source, char value, int startIndex)
    {
        return IndexOf(source, value, startIndex, CompareOptions.None);
    }

    public int IndexOf(string source, string value, int startIndex)
    {
        return IndexOf(source, value, startIndex, CompareOptions.None);
    }

    public int IndexOf(string source, char value, int startIndex, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        return IndexOf(source, value, startIndex, source.Length - startIndex, options);
    }

    public int IndexOf(string source, string value, int startIndex, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        return IndexOf(source, value, startIndex, source.Length - startIndex, options);
    }

    public int IndexOf(string source, char value, int startIndex, int count)
    {
        return IndexOf(source, value, startIndex, count, CompareOptions.None);
    }

    public int IndexOf(string source, string value, int startIndex, int count)
    {
        return IndexOf(source, value, startIndex, count, CompareOptions.None);
    }

    public int IndexOf(string source, char value, int startIndex, int count, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        ReadOnlySpan<char> sourceSpan = GetIndexOfSpan(source, startIndex, count);
        int result = IndexOf(sourceSpan, new ReadOnlySpan<char>(in value), options);
        if (result >= 0)
        {
            result += startIndex;
        }
        return result;
    }

    public int IndexOf(string source, string value, int startIndex, int count, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(value);

        ReadOnlySpan<char> sourceSpan = GetIndexOfSpan(source, startIndex, count);
        int result = IndexOf(sourceSpan, value.AsSpan(), options);
        if (result >= 0)
        {
            result += startIndex;
        }
        return result;
    }

    // String.TryGetSpan and its bounds failures, as upstream's IndexOf overloads surface them.
    private static ReadOnlySpan<char> GetIndexOfSpan(string source, int startIndex, int count)
    {
        if ((uint)startIndex > (uint)source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex), SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
        }

        if ((uint)count > (uint)(source.Length - startIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(count), SR.ArgumentOutOfRange_Count);
        }

        return source.AsSpan(startIndex, count);
    }

    // Invariant mode matches ordinally, so a match is as long as the value.
    public int IndexOf(ReadOnlySpan<char> source, ReadOnlySpan<char> value, CompareOptions options, out int matchLength)
    {
        int index = IndexOf(source, value, options);
        matchLength = index >= 0 ? value.Length : 0;
        return index;
    }

    public int IndexOf(ReadOnlySpan<char> source, ReadOnlySpan<char> value, CompareOptions options = CompareOptions.None)
    {
        if ((options & ValidIndexMaskOffFlags) == 0)
        {
            // Invariant mode normalizes the flags to Ordinal[IgnoreCase].
            if ((options & CompareOptions.IgnoreCase) == 0)
            {
                return source.IndexOf(value);
            }

            return Ordinal.IndexOfOrdinalIgnoreCase(source, value);
        }

        if (options == CompareOptions.Ordinal)
        {
            return source.IndexOf(value);
        }

        if (options == CompareOptions.OrdinalIgnoreCase)
        {
            return Ordinal.IndexOfOrdinalIgnoreCase(source, value);
        }

        throw new ArgumentException(SR.Argument_InvalidFlag, nameof(options));
    }

    public int LastIndexOf(string source, char value)
    {
        return LastIndexOf(source, value, CompareOptions.None);
    }

    public int LastIndexOf(string source, string value)
    {
        return LastIndexOf(source, value, CompareOptions.None);
    }

    public int LastIndexOf(string source, char value, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        return LastIndexOf(source.AsSpan(), new ReadOnlySpan<char>(in value), options);
    }

    public int LastIndexOf(string source, string value, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(value);

        return LastIndexOf(source.AsSpan(), value.AsSpan(), options);
    }

    public int LastIndexOf(string source, char value, int startIndex)
    {
        return LastIndexOf(source, value, startIndex, startIndex + 1, CompareOptions.None);
    }

    public int LastIndexOf(string source, string value, int startIndex)
    {
        return LastIndexOf(source, value, startIndex, startIndex + 1, CompareOptions.None);
    }

    public int LastIndexOf(string source, char value, int startIndex, CompareOptions options)
    {
        return LastIndexOf(source, value, startIndex, startIndex + 1, options);
    }

    public int LastIndexOf(string source, string value, int startIndex, CompareOptions options)
    {
        return LastIndexOf(source, value, startIndex, startIndex + 1, options);
    }

    public int LastIndexOf(string source, char value, int startIndex, int count)
    {
        return LastIndexOf(source, value, startIndex, count, CompareOptions.None);
    }

    public int LastIndexOf(string source, string value, int startIndex, int count)
    {
        return LastIndexOf(source, value, startIndex, count, CompareOptions.None);
    }

    public int LastIndexOf(string source, char value, int startIndex, int count, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        ReadOnlySpan<char> sourceSpan = GetLastIndexOfSpan(source, ref startIndex, count);
        int retVal = LastIndexOf(sourceSpan, new ReadOnlySpan<char>(in value), options);
        if (retVal >= 0)
        {
            retVal += startIndex;
        }
        return retVal;
    }

    public int LastIndexOf(string source, string value, int startIndex, int count, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(value);

        ReadOnlySpan<char> sourceSpan = GetLastIndexOfSpan(source, ref startIndex, count);
        int retVal = LastIndexOf(sourceSpan, value.AsSpan(), options);
        if (retVal >= 0)
        {
            retVal += startIndex;
        }
        return retVal;
    }

    // Upstream's LastIndexOf parameter fix-ups: on return, startIndex is where the search region begins.
    private static ReadOnlySpan<char> GetLastIndexOfSpan(string source, ref int startIndex, int count)
    {
    TryAgain:

        // Previous versions of the Framework special-cased empty 'source' to allow startIndex = -1 or startIndex = 0,
        // ignoring 'count' and short-circuiting the entire operation. We'll silently fix up the 'count' parameter
        // if this occurs.
        if ((uint)startIndex >= (uint)source.Length)
        {
            if (startIndex == -1 && source.Length == 0)
            {
                count = 0; // normalize
            }
            else if (startIndex == source.Length)
            {
                // The caller likely had an off-by-one error when invoking the API. The Framework has historically
                // allowed for this and tried to fix up the parameters, so we'll continue to do so for compat.

                startIndex--;
                if (count > 0)
                {
                    count--;
                }

                goto TryAgain; // guaranteed never to loop more than once
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex), SR.ArgumentOutOfRange_IndexMustBeLess);
            }
        }

        startIndex = startIndex - count + 1; // this will be the actual index where we begin our search

        if ((uint)startIndex > (uint)source.Length || (uint)count > (uint)(source.Length - startIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(count), SR.ArgumentOutOfRange_Count);
        }

        return source.AsSpan(startIndex, count);
    }

    public int LastIndexOf(ReadOnlySpan<char> source, ReadOnlySpan<char> value, CompareOptions options = CompareOptions.None)
    {
        if ((options & ValidIndexMaskOffFlags) == 0)
        {
            // Invariant mode normalizes the flags to Ordinal[IgnoreCase].
            if ((options & CompareOptions.IgnoreCase) == 0)
            {
                return source.LastIndexOf(value);
            }

            return Ordinal.LastIndexOfOrdinalIgnoreCase(source, value);
        }

        if (options == CompareOptions.Ordinal)
        {
            return source.LastIndexOf(value);
        }

        if (options == CompareOptions.OrdinalIgnoreCase)
        {
            return Ordinal.LastIndexOfOrdinalIgnoreCase(source, value);
        }

        throw new ArgumentException(SR.Argument_InvalidFlag, nameof(options));
    }

    public SortKey GetSortKey(string source, CompareOptions options) => InvariantCreateSortKey(source, options);

    public SortKey GetSortKey(string source) => InvariantCreateSortKey(source, CompareOptions.None);

    private SortKey InvariantCreateSortKey(string source, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Upstream's ValidCompareMaskOffFlags: Ordinal and OrdinalIgnoreCase are not sort-key options.
        if ((options & ~ValidCompareOptions) != 0)
        {
            throw new ArgumentException(SR.Argument_InvalidFlag, nameof(options));
        }

        byte[] keyData;
        if (source.Length == 0)
        {
            keyData = [];
        }
        else
        {
            // In the invariant mode, all string comparisons are done as ordinal so when generating the sort keys we generate it according to this fact
            keyData = new byte[source.Length * sizeof(char)];

            // Ignore-case keys use invariant upper casing, which for a surrogate pair maps the pair as one code point.
            string text = (options & CompareOptions.IgnoreCase) != 0 ? source.ToUpperInvariant() : source;
            Span<byte> sortKey = keyData;
            for (int i = 0; i < text.Length; i++)
            {
                // convert machine-endian to big-endian
                BinaryPrimitives.WriteUInt16BigEndian(sortKey, (ushort)text[i]);
                sortKey = sortKey.Slice(sizeof(ushort));
            }
        }

        return new SortKey(this, source, options, keyData);
    }

    public override bool Equals([NotNullWhen(true)] object? value)
    {
        return value is CompareInfo otherCompareInfo
            && Name == otherCompareInfo.Name;
    }

    public override int GetHashCode() => Name.GetHashCode();

    public int GetHashCode(string source, CompareOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.GetHashCode(InvariantComparison(options));
    }

    public int GetHashCode(ReadOnlySpan<char> source, CompareOptions options) =>
        string.GetHashCode(source, InvariantComparison(options));

    public override string ToString() => "CompareInfo - " + Name;

    public SortVersion Version
    {
        get
        {
            return m_SortVersion ??= new SortVersion(0, CultureInfo.LOCALE_INVARIANT, new Guid(0, 0, 0, 0, 0, 0, 0,
                (byte)(CultureInfo.LOCALE_INVARIANT >> 24),
                (byte)((CultureInfo.LOCALE_INVARIANT & 0x00FF0000) >> 16),
                (byte)((CultureInfo.LOCALE_INVARIANT & 0x0000FF00) >> 8),
                (byte)(CultureInfo.LOCALE_INVARIANT & 0xFF)));
        }
    }

    public int LCID => CultureInfo.GetCultureInfo(Name).LCID;

    private static StringComparison InvariantComparison(CompareOptions options)
    {
        // This is the upstream invariant-runtime contract, not linguistic
        // collation for InvariantCulture in an ICU/NLS-enabled runtime.
        if ((options & ~ValidCompareOptions) == 0)
            return (options & CompareOptions.IgnoreCase) == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (options == CompareOptions.Ordinal)
            return StringComparison.Ordinal;
        if (options == CompareOptions.OrdinalIgnoreCase)
            return StringComparison.OrdinalIgnoreCase;
        throw new ArgumentException("Invalid comparison options.", nameof(options));
    }

    // The IsPrefix/IsSuffix comparison for the given options, as upstream's invariant mode chooses it.
    private static StringComparison IndexComparison(CompareOptions options)
    {
        if ((options & ValidIndexMaskOffFlags) == 0)
        {
            return (options & CompareOptions.IgnoreCase) == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        if (options == CompareOptions.Ordinal)
        {
            return StringComparison.Ordinal;
        }

        if (options == CompareOptions.OrdinalIgnoreCase)
        {
            return StringComparison.OrdinalIgnoreCase;
        }

        throw new ArgumentException(
            paramName: nameof(options),
            message: ((options & CompareOptions.Ordinal) != 0) ? SR.Argument_CompareOptionOrdinal : SR.Argument_InvalidFlag);
    }
}
