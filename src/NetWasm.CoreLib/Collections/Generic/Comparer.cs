// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib Comparer.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    public abstract class Comparer<T> : IComparer<T>, System.Collections.IComparer
    {
        private static readonly Comparer<T> s_default = new ObjectComparer<T>();

        public static Comparer<T> Default
        {
            get => s_default;
        }

        public static Comparer<T> Create(System.Comparison<T> comparison)
        {
            if (comparison == null)
            {
                throw new System.ArgumentNullException();
            }
            return new ComparisonComparer<T>(comparison);
        }

        public abstract int Compare(T? left, T? right);

        int System.Collections.IComparer.Compare(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }
            if (left is null)
            {
                return -1;
            }
            if (right is null)
            {
                return 1;
            }
            if (left is T typedLeft && right is T typedRight)
            {
                return Compare(typedLeft, typedRight);
            }
            throw new System.ArgumentException();
        }

        private sealed class ComparisonComparer<TValue> : Comparer<TValue>
        {
            private readonly System.Comparison<TValue> _comparison;

            internal ComparisonComparer(System.Comparison<TValue> comparison) => _comparison = comparison;

            public override int Compare(TValue? left, TValue? right) => _comparison(left!, right!);
        }
    }

    public sealed class GenericComparer<T> : Comparer<T>
        where T : System.IComparable<T>
    {
        public GenericComparer()
        {
        }

        public override int Compare(T? left, T? right)
        {
            if (left is null)
            {
                return right is null ? 0 : -1;
            }
            if (right is null)
            {
                return 1;
            }
            if (left is System.IComparable<T> comparable)
            {
                return comparable.CompareTo(right);
            }
            if (left is System.IComparable nonGeneric)
            {
                return nonGeneric.CompareTo(right);
            }
            throw new System.ArgumentException();
        }

        public override bool Equals(object? value) => value is GenericComparer<T>;

        public override int GetHashCode() => 0;
    }

    public sealed class ObjectComparer<T> : Comparer<T>
    {
        public ObjectComparer()
        {
        }

        public override int Compare(T? left, T? right)
        {
            if (left is null)
            {
                return right is null ? 0 : -1;
            }
            if (right is null)
            {
                return 1;
            }
            if (left is System.IComparable<T> comparable)
            {
                return comparable.CompareTo(right);
            }
            if (left is System.IComparable nonGeneric)
            {
                return nonGeneric.CompareTo(right);
            }
            throw new System.ArgumentException();
        }

        public override bool Equals(object? value) => value is ObjectComparer<T>;

        public override int GetHashCode() => 0;
    }
}
