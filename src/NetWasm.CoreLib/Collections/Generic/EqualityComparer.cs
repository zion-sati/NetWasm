// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib EqualityComparer.cs at
// commit 474ed963699fc29809015392738a15231aaae2de.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    public abstract class EqualityComparer<T> : IEqualityComparer<T>, System.Collections.IEqualityComparer
    {
        private static readonly EqualityComparer<T> s_default = CreateDefaultComparer();

        public static EqualityComparer<T> Default
        {
            [Intrinsic]
            get => s_default;
        }

        // The AOT compiler replaces this call for closed types. Reflection-free
        // open generic code retains the object-comparer fallback.
        [Intrinsic]
        private static EqualityComparer<T> CreateDefaultComparer() =>
            new ObjectEqualityComparer<T>();

        public static EqualityComparer<T> Create(
            Func<T?, T?, bool> equals,
            Func<T, int>? getHashCode = null)
        {
            ArgumentNullException.ThrowIfNull(equals);
            getHashCode ??= _ => throw new NotSupportedException();
            return new DelegateEqualityComparer<T>(equals, getHashCode);
        }

        public static EqualityComparer<T> Create<TKey>(
            Func<T?, TKey?> keySelector,
            IEqualityComparer<TKey>? keyComparer = null)
        {
            ArgumentNullException.ThrowIfNull(keySelector);
            keyComparer ??= EqualityComparer<TKey>.Default;
            return new DelegateEqualityComparer<T>(
                (itemX, itemY) => keyComparer.Equals(keySelector(itemX), keySelector(itemY)),
                obj =>
                {
                    var key = keySelector(obj);
                    return key is null ? 0 : keyComparer.GetHashCode(key);
                });
        }

        public abstract bool Equals(T? x, T? y);

        public abstract int GetHashCode([DisallowNull] T obj);

        int System.Collections.IEqualityComparer.GetHashCode(object obj)
        {
            if (obj == null)
            {
                return 0;
            }
            if (obj is T typedObj)
            {
                return GetHashCode(typedObj);
            }
            throw new ArgumentException();
        }

        bool System.Collections.IEqualityComparer.Equals(object? x, object? y)
        {
            if (x == y)
            {
                return true;
            }
            if (x == null || y == null)
            {
                return false;
            }
            if (x is T typedX && y is T typedY)
            {
                return Equals(typedX, typedY);
            }
            throw new ArgumentException();
        }

        internal virtual int IndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex + count;
            for (var index = startIndex; index < endIndex; index++)
            {
                if (Equals(array[index], value))
                {
                    return index;
                }
            }
            return -1;
        }

        internal virtual int LastIndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex - count + 1;
            for (var index = startIndex; index >= endIndex; index--)
            {
                if (Equals(array[index], value))
                {
                    return index;
                }
            }
            return -1;
        }
    }

    internal sealed class DelegateEqualityComparer<T> : EqualityComparer<T>
    {
        private readonly Func<T?, T?, bool> _equals;
        private readonly Func<T, int> _getHashCode;

        public DelegateEqualityComparer(
            Func<T?, T?, bool> equals,
            Func<T, int> getHashCode)
        {
            _equals = equals;
            _getHashCode = getHashCode;
        }

        public override bool Equals(T? x, T? y) => _equals(x, y);

        public override int GetHashCode([DisallowNull] T obj) => _getHashCode(obj);

        public override bool Equals(object? obj) =>
            obj is DelegateEqualityComparer<T> other &&
            _equals == other._equals &&
            _getHashCode == other._getHashCode;

        public override int GetHashCode() =>
            HashCode.Combine(_equals.GetHashCode(), _getHashCode.GetHashCode());
    }

    public sealed class GenericEqualityComparer<T> : EqualityComparer<T> where T : IEquatable<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T? x, T? y)
        {
            if (x != null)
            {
                return y != null && x.Equals(y);
            }
            return y == null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode([DisallowNull] T obj) =>
            obj?.GetHashCode() ?? 0;

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is GenericEqualityComparer<T>;

        public override int GetHashCode() => 0;

        internal override int IndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex + count;
            if (value == null)
            {
                for (var index = startIndex; index < endIndex; index++)
                {
                    if (array[index] == null)
                    {
                        return index;
                    }
                }
            }
            else
            {
                for (var index = startIndex; index < endIndex; index++)
                {
                    if (array[index] != null && array[index]!.Equals(value))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }

        internal override int LastIndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex - count + 1;
            if (value == null)
            {
                for (var index = startIndex; index >= endIndex; index--)
                {
                    if (array[index] == null)
                    {
                        return index;
                    }
                }
            }
            else
            {
                for (var index = startIndex; index >= endIndex; index--)
                {
                    if (array[index] != null && array[index]!.Equals(value))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }
    }

    public sealed class NullableEqualityComparer<T> : EqualityComparer<T?> where T : struct
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T? x, T? y)
        {
            if (x.HasValue)
            {
                return y.HasValue && EqualityComparer<T>.Default.Equals(x.value, y.value);
            }
            return !y.HasValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(T? obj) => obj.GetHashCode();

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is NullableEqualityComparer<T>;

        public override int GetHashCode() => 0;

        internal override int IndexOf(T?[] array, T? value, int startIndex, int count)
        {
            var endIndex = startIndex + count;
            if (!value.HasValue)
            {
                for (var index = startIndex; index < endIndex; index++)
                {
                    if (!array[index].HasValue)
                    {
                        return index;
                    }
                }
            }
            else
            {
                for (var index = startIndex; index < endIndex; index++)
                {
                    if (array[index].HasValue &&
                        EqualityComparer<T>.Default.Equals(array[index].value, value.value))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }

        internal override int LastIndexOf(T?[] array, T? value, int startIndex, int count)
        {
            var endIndex = startIndex - count + 1;
            if (!value.HasValue)
            {
                for (var index = startIndex; index >= endIndex; index--)
                {
                    if (!array[index].HasValue)
                    {
                        return index;
                    }
                }
            }
            else
            {
                for (var index = startIndex; index >= endIndex; index--)
                {
                    if (array[index].HasValue &&
                        EqualityComparer<T>.Default.Equals(array[index].value, value.value))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }
    }

    public sealed class ObjectEqualityComparer<T> : EqualityComparer<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T? x, T? y)
        {
            if (x != null)
            {
                return y != null && x.Equals(y);
            }
            return y == null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode([DisallowNull] T obj) =>
            obj?.GetHashCode() ?? 0;

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is ObjectEqualityComparer<T>;

        public override int GetHashCode() => 0;

        internal override int IndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex + count;
            if (value == null)
            {
                for (var index = startIndex; index < endIndex; index++)
                {
                    if (array[index] == null)
                    {
                        return index;
                    }
                }
            }
            else
            {
                for (var index = startIndex; index < endIndex; index++)
                {
                    if (array[index] != null && array[index]!.Equals(value))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }

        internal override int LastIndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex - count + 1;
            if (value == null)
            {
                for (var index = startIndex; index >= endIndex; index--)
                {
                    if (array[index] == null)
                    {
                        return index;
                    }
                }
            }
            else
            {
                for (var index = startIndex; index >= endIndex; index--)
                {
                    if (array[index] != null && array[index]!.Equals(value))
                    {
                        return index;
                    }
                }
            }
            return -1;
        }
    }

    public sealed class ByteEqualityComparer : EqualityComparer<byte>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(byte x, byte y) => x == y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(byte b) => b.GetHashCode();

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is ByteEqualityComparer;

        public override int GetHashCode() => 0;

#if DEBUG
        internal override int IndexOf(byte[] array, byte value, int startIndex, int count)
        {
            Debug.Fail("Should not get here.");
            return -1;
        }

        internal override int LastIndexOf(byte[] array, byte value, int startIndex, int count)
        {
            Debug.Fail("Should not get here.");
            return -1;
        }
#endif
    }

    public sealed class EnumEqualityComparer<T> : EqualityComparer<T> where T : struct, Enum
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(T x, T y) => x.Equals(y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode(T obj) => obj.GetHashCode();

        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is EnumEqualityComparer<T>;

        public override int GetHashCode() => 0;

        internal override int IndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex + count;
            for (var index = startIndex; index < endIndex; index++)
            {
                if (array[index].Equals(value))
                {
                    return index;
                }
            }
            return -1;
        }

        internal override int LastIndexOf(T[] array, T value, int startIndex, int count)
        {
            var endIndex = startIndex - count + 1;
            for (var index = startIndex; index >= endIndex; index--)
            {
                if (array[index].Equals(value))
                {
                    return index;
                }
            }
            return -1;
        }
    }

    internal sealed class StringEqualityComparer :
        EqualityComparer<string>,
        IAlternateEqualityComparer<ReadOnlySpan<char>, string>
    {
        public override bool Equals(string? x, string? y) => string.Equals(x, y);

        public override int GetHashCode([DisallowNull] string obj) =>
            obj?.GetHashCode() ?? 0;

        public bool Equals(ReadOnlySpan<char> alternate, string target)
        {
            if (alternate.IsEmpty && target is null)
            {
                return false;
            }
            return alternate.SequenceEqual(target);
        }

        public int GetHashCode(ReadOnlySpan<char> alternate) =>
            string.GetHashCode(alternate);

        public string Create(ReadOnlySpan<char> alternate) => alternate.ToString();

        public override bool Equals(object? obj) => obj is StringEqualityComparer;

        public override int GetHashCode() => 0;
    }
}
