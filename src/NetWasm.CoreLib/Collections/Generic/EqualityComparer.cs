// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib EqualityComparer.cs at
// commit 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    public abstract class EqualityComparer<T> : IEqualityComparer<T>, System.Collections.IEqualityComparer
    {
        private static readonly EqualityComparer<T> DefaultComparer = new DefaultEqualityComparer();

        public static EqualityComparer<T> Default
        {
            get => DefaultComparer;
        }

        public static EqualityComparer<T> Create(
            System.Func<T?, T?, bool> equals,
            System.Func<T, int>? getHashCode = null)
        {
            if (equals == null)
            {
                throw new System.ArgumentNullException();
            }

            return new DelegateEqualityComparer<T>(
                equals,
                getHashCode ?? (_ => throw new System.NotSupportedException()));
        }

        public static EqualityComparer<T> Create<TKey>(
            System.Func<T?, TKey?> keySelector,
            IEqualityComparer<TKey>? keyComparer = null)
        {
            if (keySelector == null)
            {
                throw new System.ArgumentNullException();
            }

            keyComparer ??= EqualityComparer<TKey>.Default;
            return new DelegateEqualityComparer<T>(
                (left, right) => keyComparer.Equals(keySelector(left), keySelector(right)),
                value =>
                {
                    var key = keySelector(value);
                    return key is null ? 0 : keyComparer.GetHashCode(key);
                });
        }

        public abstract bool Equals(T? left, T? right);

        public abstract int GetHashCode(T value);

        bool System.Collections.IEqualityComparer.Equals(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left is null || right is null)
            {
                return false;
            }
            if (left is T typedLeft && right is T typedRight)
            {
                return Equals(typedLeft, typedRight);
            }
            throw new System.ArgumentException();
        }

        int System.Collections.IEqualityComparer.GetHashCode(object value)
        {
            if (value is null)
            {
                return 0;
            }
            if (value is T typedValue)
            {
                return GetHashCode(typedValue);
            }
            throw new System.ArgumentException();
        }

        private sealed class DefaultEqualityComparer : EqualityComparer<T>
        {
            public override bool Equals(T? left, T? right)
            {
                if (left is null)
                {
                    return right is null;
                }
                if (right is null)
                {
                    return false;
                }
                return left is System.IEquatable<T> equatable
                    ? equatable.Equals(right)
                    : ((object)left).Equals(right);
            }

            public override int GetHashCode(T value) =>
                value is null ? 0 : ((object)value).GetHashCode();
        }
    }

    internal sealed class DelegateEqualityComparer<T> : EqualityComparer<T>
    {
        private readonly System.Func<T?, T?, bool> _equals;
        private readonly System.Func<T, int> _getHashCode;

        internal DelegateEqualityComparer(
            System.Func<T?, T?, bool> equals,
            System.Func<T, int> getHashCode)
        {
            _equals = equals;
            _getHashCode = getHashCode;
        }

        public override bool Equals(T? left, T? right) => _equals(left, right);

        public override int GetHashCode(T value) => _getHashCode(value);
    }

    // Public comparer classes are retained because closed generic code and the
    // desktop API expose their concrete identities. Runtime-driven comparer
    // discovery is intentionally not used by NetWasm's AOT profile.
    public sealed class GenericEqualityComparer<T> : EqualityComparer<T>
        where T : System.IEquatable<T>
    {
        public GenericEqualityComparer()
        {
        }

        public override bool Equals(T? left, T? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return right is not null && left.Equals(right);
        }

        public override int GetHashCode(T value) =>
            value is null ? 0 : value.GetHashCode();

        public override bool Equals(object? value) => value is GenericEqualityComparer<T>;

        public override int GetHashCode() => 0;
    }

    public sealed class ObjectEqualityComparer<T> : EqualityComparer<T>
    {
        public ObjectEqualityComparer()
        {
        }

        public override bool Equals(T? left, T? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return right is not null && ((object)left).Equals(right);
        }

        public override int GetHashCode(T value) =>
            value is null ? 0 : ((object)value).GetHashCode();

        public override bool Equals(object? value) => value is ObjectEqualityComparer<T>;

        public override int GetHashCode() => 0;
    }

    public sealed class ByteEqualityComparer : EqualityComparer<byte>
    {
        public ByteEqualityComparer()
        {
        }

        public override bool Equals(byte left, byte right) => left == right;

        public override int GetHashCode(byte value) => value.GetHashCode();

        public override bool Equals(object? value) => value is ByteEqualityComparer;

        public override int GetHashCode() => 0;
    }
}
