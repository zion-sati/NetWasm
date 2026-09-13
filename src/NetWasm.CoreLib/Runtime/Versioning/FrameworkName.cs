// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic port of dotnet/runtime System.Private.CoreLib FrameworkName.cs.
// Upstream commit: 811225a482702af7ecc35d817966bc70b88a3a23.
// Reflection and culture-specific services are not required by this managed parser.

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.Versioning;

public sealed class FrameworkName : IEquatable<FrameworkName?>
{
    private readonly string _identifier;
    private readonly Version _version;
    private readonly string _profile;
    private string? _fullName;

    private const char ComponentSeparator = ',';
    private const char KeyValueSeparator = '=';
    private const char VersionValuePrefix = 'v';
    private const string VersionKey = "Version";
    private const string ProfileKey = "Profile";

    public string Identifier
    {
        get => _identifier;
    }

    public Version Version
    {
        get => _version;
    }

    public string Profile
    {
        get => _profile;
    }

    public string FullName
    {
        get
        {
            _fullName ??= string.IsNullOrEmpty(Profile)
                ? $"{Identifier}, Version=v{Version}"
                : $"{Identifier}, Version=v{Version}, Profile={Profile}";
            return _fullName;
        }
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as FrameworkName);

    public bool Equals([NotNullWhen(true)] FrameworkName? other) =>
        other is not null && Identifier == other.Identifier && Version == other.Version && Profile == other.Profile;

    public override int GetHashCode() => Identifier.GetHashCode() ^ Version.GetHashCode() ^ Profile.GetHashCode();

    public override string ToString() => FullName;

    public FrameworkName(string identifier, Version version)
        : this(identifier, version, null)
    {
    }

    public FrameworkName(string identifier, Version version, string? profile)
    {
        identifier = identifier?.Trim()!;
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        ArgumentNullException.ThrowIfNull(version);
        _identifier = identifier;
        _version = version;
        _profile = profile is null ? string.Empty : profile.Trim();
    }

    // Parses: "<identifier>, Version=[v|V]<version>, Profile=<profile>".
    // Identifier and version are required; profile is optional and components may
    // appear in either order after the identifier.
    public FrameworkName(string frameworkName)
    {
        ArgumentException.ThrowIfNullOrEmpty(frameworkName);
        var parts = new string[4];
        var count = 0;
        var start = 0;
        for (var index = 0; index <= frameworkName.Length; index++)
        {
            if (index != frameworkName.Length && frameworkName[index] != ComponentSeparator)
            {
                continue;
            }

            if (count == parts.Length)
            {
                throw InvalidFrameworkName(frameworkName);
            }

            parts[count++] = frameworkName.Substring(start, index - start);
            start = index + 1;
        }

        if (count is not (2 or 3))
        {
            throw InvalidFrameworkName(frameworkName);
        }

        _identifier = parts[0].Trim();
        if (_identifier.Length == 0)
        {
            throw InvalidFrameworkName(frameworkName);
        }

        _profile = string.Empty;
        _version = null!;
        var versionFound = false;
        for (var index = 1; index < count; index++)
        {
            var component = parts[index].AsSpan();
            var separator = component.IndexOf(KeyValueSeparator);
            if (separator < 0 || separator != component.LastIndexOf(KeyValueSeparator))
            {
                throw InvalidFrameworkName(frameworkName);
            }

            var key = component[..separator].Trim().ToString();
            var value = component[(separator + 1)..].Trim().ToString();
            if (string.Equals(key, VersionKey, StringComparison.OrdinalIgnoreCase))
            {
                versionFound = true;
                if (value.Length > 0 && (value[0] == VersionValuePrefix || value[0] == 'V'))
                {
                    value = value.Substring(1);
                }

                try
                {
                    _version = Version.Parse(value);
                }
                catch (Exception exception)
                {
                    throw new ArgumentException("Framework name contains an invalid version.", nameof(frameworkName), exception);
                }
            }
            else if (string.Equals(key, ProfileKey, StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0)
                {
                    _profile = value;
                }
            }
            else
            {
                throw InvalidFrameworkName(frameworkName);
            }
        }

        if (!versionFound)
        {
            throw new ArgumentException("Framework name is missing its Version component.", nameof(frameworkName));
        }
    }

    private static ArgumentException InvalidFrameworkName(string frameworkName) =>
        new("Framework name is invalid.", nameof(frameworkName));

    public static bool operator ==(FrameworkName? left, FrameworkName? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(FrameworkName? left, FrameworkName? right) => !(left == right);
}
