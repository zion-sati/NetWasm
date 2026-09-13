// Portions derived from dotnet/runtime System.Private.CoreLib StringComparer.
// Alternate ordinal-comparer members are from dotnet/runtime commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;

namespace System
{
    /// <summary>Compares strings using a specified ordinal comparison.</summary>
    public abstract class StringComparer : IComparer<string>, IEqualityComparer<string>, IComparer, IEqualityComparer
    {
        public static StringComparer Ordinal
        {
            get { return OrdinalComparer.Instance; }
        }

        public static StringComparer OrdinalIgnoreCase
        {
            get { return OrdinalComparer.IgnoreCaseInstance; }
        }

        // Culture-sensitive comparers are intentionally outside NetWasm's
        // ordinal text profile. Keep the framework surface explicit while
        // making accidental culture dependence fail at the boundary.
        public static StringComparer CurrentCulture
        {
            get { throw new PlatformNotSupportedException(); }
        }
        public static StringComparer CurrentCultureIgnoreCase
        {
            get { throw new PlatformNotSupportedException(); }
        }
        public static StringComparer InvariantCulture
        {
            get { throw new PlatformNotSupportedException(); }
        }
        public static StringComparer InvariantCultureIgnoreCase
        {
            get { throw new PlatformNotSupportedException(); }
        }

        public static StringComparer FromComparison(StringComparison comparisonType) => comparisonType switch
        {
            StringComparison.Ordinal => Ordinal,
            StringComparison.OrdinalIgnoreCase => OrdinalIgnoreCase,
            _ => throw new PlatformNotSupportedException(),
        };

        public static bool IsWellKnownOrdinalComparer(IEqualityComparer<string>? comparer, out bool ignoreCase)
        {
            if (ReferenceEquals(comparer, EqualityComparer<string>.Default))
            {
                ignoreCase = false;
                return true;
            }

            if (comparer is OrdinalComparer ordinal)
            {
                ignoreCase = ordinal.IgnoreCase;
                return true;
            }

            ignoreCase = false;
            return false;
        }

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }
            if (x is null)
            {
                return -1;
            }
            if (y is null)
            {
                return 1;
            }
            if (x is string left && y is string right)
            {
                return Compare(left, right);
            }
            if (x is IComparable comparable)
            {
                return comparable.CompareTo(y);
            }
            throw new ArgumentException();
        }

        public new bool Equals(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null)
            {
                return false;
            }
            return x is string left && y is string right
                ? Equals(left, right)
                : x.Equals(y);
        }

        public int GetHashCode(object obj)
        {
            if (obj is null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            return obj is string text ? GetHashCode(text) : obj.GetHashCode();
        }

        public abstract int Compare(string? x, string? y);
        public abstract bool Equals(string? x, string? y);
        public abstract int GetHashCode(string obj);
    }

    /// <summary>Ordinal string comparer, optionally ignoring case.</summary>
    public sealed class OrdinalComparer : StringComparer,
        IAlternateEqualityComparer<ReadOnlySpan<char>, string>
    {
        internal static readonly OrdinalComparer Instance = new(false);
        internal static readonly OrdinalComparer IgnoreCaseInstance = new(true);

        private readonly bool _ignoreCase;

        private OrdinalComparer(bool ignoreCase) => _ignoreCase = ignoreCase;

        internal bool IgnoreCase => _ignoreCase;

        public override int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }
            if (x is null)
            {
                return -1;
            }
            if (y is null)
            {
                return 1;
            }

            var length = Math.Min(x.Length, y.Length);
            for (var index = 0; index < length; index++)
            {
                var left = Fold(x[index]);
                var right = Fold(y[index]);
                if (left != right)
                {
                    return left - right;
                }
            }
            return x.Length - y.Length;
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }
            if (x is null || y is null || x.Length != y.Length)
            {
                return false;
            }
            for (var index = 0; index < x.Length; index++)
            {
                if (Fold(x[index]) != Fold(y[index]))
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode(string obj)
        {
            if (obj is null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            var hash = 2_166_136_261u;
            for (var index = 0; index < obj.Length; index++)
            {
                hash = (hash ^ Fold(obj[index])) * 16_777_619u;
            }
            hash ^= hash >> 16;
            hash *= 2_246_822_519u;
            hash ^= hash >> 13;
            hash *= 3_266_489_917u;
            hash ^= hash >> 16;
            return (int)hash;
        }

        public override bool Equals(object? obj) => obj is OrdinalComparer other && _ignoreCase == other._ignoreCase;

        public override int GetHashCode() => _ignoreCase ? 1 : 0;

        bool IAlternateEqualityComparer<ReadOnlySpan<char>, string>.Equals(
            ReadOnlySpan<char> alternate,
            string? target)
        {
            if (alternate.Length == 0 && target is null)
            {
                return false;
            }
            if (target is null || alternate.Length != target.Length)
            {
                return false;
            }
            for (var index = 0; index < alternate.Length; index++)
            {
                if (Fold(alternate[index]) != Fold(target[index]))
                {
                    return false;
                }
            }
            return true;
        }

        int IAlternateEqualityComparer<ReadOnlySpan<char>, string>.GetHashCode(
            ReadOnlySpan<char> alternate)
        {
            var hash = 2_166_136_261u;
            for (var index = 0; index < alternate.Length; index++)
            {
                hash = (hash ^ Fold(alternate[index])) * 16_777_619u;
            }
            hash ^= hash >> 16;
            hash *= 2_246_822_519u;
            hash ^= hash >> 13;
            hash *= 3_266_489_917u;
            hash ^= hash >> 16;
            return (int)hash;
        }

        string IAlternateEqualityComparer<ReadOnlySpan<char>, string>.Create(
            ReadOnlySpan<char> alternate) => alternate.ToString();

        private char Fold(char value)
        {
            if (!_ignoreCase)
            {
                return value;
            }
            return char.ToUpperInvariant(value);
        }
    }
}
