// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib Comparer.cs at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    public abstract class Comparer<T> : IComparer<T>, System.Collections.IComparer
    {
        private static readonly Comparer<T> s_default = CreateDefaultComparer();

        public static Comparer<T> Default
        {
            [Intrinsic]
            get => s_default;
        }

        // The AOT compiler replaces this call for closed types. Reflection-free
        // open generic code retains the object-comparer fallback.
        [Intrinsic]
        private static Comparer<T> CreateDefaultComparer() => new ObjectComparer<T>();

        public static Comparer<T> Create(Comparison<T> comparison)
        {
            ArgumentNullException.ThrowIfNull(comparison);
            return new ComparisonComparer<T>(comparison);
        }

        public abstract int Compare(T? x, T? y);

        int System.Collections.IComparer.Compare(object? x, object? y)
        {
            if (x == null)
            {
                return y == null ? 0 : -1;
            }
            if (y == null)
            {
                return 1;
            }
            if (x is T typedX && y is T typedY)
            {
                return Compare(typedX, typedY);
            }
            throw new ArgumentException();
        }
    }

    internal sealed class ComparisonComparer<T> : Comparer<T>
    {
        private readonly Comparison<T> _comparison;

        public ComparisonComparer(Comparison<T> comparison) => _comparison = comparison;

        public override int Compare(T? x, T? y) => _comparison(x!, y!);
    }

    public sealed class GenericComparer<T> : Comparer<T> where T : IComparable<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int Compare(T? x, T? y)
        {
            if (x != null)
            {
                if (y != null)
                {
                    return x.CompareTo(y);
                }
                return 1;
            }
            return y != null ? -1 : 0;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is GenericComparer<T>;

        public override int GetHashCode() => 0;
    }

    public sealed class NullableComparer<T> : Comparer<T?> where T : struct
    {
        public override int Compare(T? x, T? y)
        {
            if (x.HasValue)
            {
                if (y.HasValue)
                {
                    return Comparer<T>.Default.Compare(x.value, y.value);
                }
                return 1;
            }
            return y.HasValue ? -1 : 0;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is NullableComparer<T>;

        public override int GetHashCode() => 0;
    }

    public sealed class ObjectComparer<T> : Comparer<T>
    {
        public override int Compare(T? x, T? y)
        {
            if (x == null)
            {
                return y == null ? 0 : -1;
            }
            if (y == null)
            {
                return 1;
            }
            if (x is IComparable<T> genericComparable)
            {
                return genericComparable.CompareTo(y);
            }
            if (x is IComparable comparable)
            {
                return comparable.CompareTo(y);
            }
            throw new ArgumentException();
        }

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is ObjectComparer<T>;

        public override int GetHashCode() => 0;
    }

    internal sealed class EnumComparer<T> : Comparer<T> where T : struct, Enum
    {
        public override int Compare(T x, T y) => x.CompareTo(y);

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is EnumComparer<T>;

        public override int GetHashCode() => 0;
    }
}
