// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization;

public sealed class CultureInfo : IFormatProvider, ICloneable
{
    internal const int LOCALE_CUSTOM_UNSPECIFIED = 0x1000;
    internal const int LOCALE_CUSTOM_DEFAULT = 0x0c00;
    internal const int LOCALE_INVARIANT = 0x007F;

    private static readonly CultureInfo s_invariantCulture = new(string.Empty, validate: false);

    public CultureInfo(string name) : this(name, validate: true)
    {
    }

    private CultureInfo(string name, bool validate)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (validate && name.Length != 0)
        {
            throw new PlatformNotSupportedException();
        }

        Name = name;
    }

    public static CultureInfo InvariantCulture => s_invariantCulture;

    public static CultureInfo CurrentCulture
    {
        get => s_invariantCulture;
        set => ValidateInvariant(value);
    }

    public string Name { get; }

    public int LCID => LOCALE_INVARIANT;

    // Upstream's invariant globalization mode: every culture compares and cases as the invariant one.
    public CompareInfo CompareInfo => CompareInfo.Invariant;

    public TextInfo TextInfo => TextInfo.Invariant;

    public static CultureInfo GetCultureInfo(int culture)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(culture);
        return culture == LOCALE_INVARIANT ? s_invariantCulture : throw new PlatformNotSupportedException();
    }

    public static CultureInfo GetCultureInfo(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length == 0 ? s_invariantCulture : throw new PlatformNotSupportedException();
    }

    public NumberFormatInfo NumberFormat => NumberFormatInfo.InvariantInfo;

    public DateTimeFormatInfo DateTimeFormat => DateTimeFormatInfo.InvariantInfo;

    public object? GetFormat(Type? formatType) =>
        formatType == typeof(NumberFormatInfo) ? NumberFormat :
        formatType == typeof(DateTimeFormatInfo) ? DateTimeFormat : null;

    public override bool Equals(object? value) => value is CultureInfo culture && Name == culture.Name;

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Name;

    // NetWasm: every culture is the invariant one, so a clone is a new instance of the same culture.
    public object Clone() => new CultureInfo(Name, validate: false);

    private static void ValidateInvariant(CultureInfo value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Name.Length != 0)
        {
            throw new PlatformNotSupportedException();
        }
    }
}
