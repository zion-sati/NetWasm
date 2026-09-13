// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization;

public sealed class CultureInfo : IFormatProvider
{
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

    public object? GetFormat(Type? formatType) => formatType == typeof(CultureInfo) ? this : null;

    public override bool Equals(object? value) => value is CultureInfo culture && Name == culture.Name;

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Name;

    private static void ValidateInvariant(CultureInfo value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Name.Length != 0)
        {
            throw new PlatformNotSupportedException();
        }
    }
}
