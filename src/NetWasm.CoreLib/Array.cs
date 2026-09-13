// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System
{
    // The compiler supplies the storage and indexing representation of managed
    // arrays. Rectangular-array construction and typed Get/Set/Address calls
    // are runtime-provided members lowered by the compiler; the managed
    // members below provide the shared System.Array surface.
    public abstract class Array : ICollection, IEnumerable, IList, IStructuralComparable,
        IStructuralEquatable, ICloneable
    {
        public int Length
        {
            get => this is object[] values ? values.Length : 0;
        }
        public long LongLength
        {
            get => Length;
        }
        public int Rank
        {
            get => 1;
        }
        public static int MaxLength
        {
            get => int.MaxValue;
        }
        public bool IsFixedSize
        {
            get => true;
        }
        public bool IsReadOnly
        {
            get => false;
        }
        public bool IsSynchronized
        {
            get => false;
        }
        public object SyncRoot
        {
            get => this;
        }

        public static T[] Empty<T>() => new T[0];

        public static ReadOnlyCollection<T> AsReadOnly<T>(T[] array)
        {
            if (array is null)
            {
                throw new ArgumentNullException();
            }

            return new ReadOnlyCollection<T>(array);
        }

        public static void Clear<T>(T[] array, int index, int length)
        {
            ValidateRange(array, index, length);
            for (var current = index; current < index + length; current++)
            {
                array[current] = default!;
            }
        }

        public static void Clear(Array array)
        {
            ArgumentNullException.ThrowIfNull(array);
            InternalClear(array, 0, array.Length);
        }

        public static void Clear(Array array, int index, int length)
        {
            ArgumentNullException.ThrowIfNull(array);
            ValidateRange(array.Length, index, length);
            InternalClear(array, index, length);
        }

        public static void Copy<T>(
            T[] sourceArray,
            int sourceIndex,
            T[] destinationArray,
            int destinationIndex,
            int length)
        {
            ValidateRange(sourceArray, sourceIndex, length);
            ValidateRange(destinationArray, destinationIndex, length);
            if (ReferenceEquals(sourceArray, destinationArray) &&
                destinationIndex > sourceIndex &&
                destinationIndex < sourceIndex + length)
            {
                for (var offset = length - 1; offset >= 0; offset--)
                {
                    destinationArray[destinationIndex + offset] =
                        sourceArray[sourceIndex + offset];
                }
                return;
            }

            for (var offset = 0; offset < length; offset++)
            {
                destinationArray[destinationIndex + offset] =
                    sourceArray[sourceIndex + offset];
            }
        }

        public static void Copy(Array sourceArray, Array destinationArray, int length)
        {
            Copy(sourceArray, 0, destinationArray, 0, length);
        }

        public static void Copy(Array sourceArray, Array destinationArray, long length)
        {
            Copy(sourceArray, destinationArray, ToInt32(length));
        }

        public static void Copy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            ValidateRange(sourceArray.Length, sourceIndex, length);
            ValidateRange(destinationArray.Length, destinationIndex, length);
            if (!InternalCopy(
                    sourceArray,
                    sourceIndex,
                    destinationArray,
                    destinationIndex,
                    length))
            {
                throw new ArrayTypeMismatchException();
            }
        }

        public static void Copy(
            Array sourceArray,
            long sourceIndex,
            Array destinationArray,
            long destinationIndex,
            long length)
        {
            Copy(
                sourceArray,
                ToInt32(sourceIndex),
                destinationArray,
                ToInt32(destinationIndex),
                ToInt32(length));
        }

        public static void Resize<T>([NotNull] ref T[]? array, int newSize)
        {
            if (newSize < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            var replacement = new T[newSize];
            if (array is not null)
            {
                var length = array.Length < newSize ? array.Length : newSize;
                Copy(array, 0, replacement, 0, length);
            }
            array = replacement;
        }

        public static int IndexOf<T>(T[] array, T value) =>
            IndexOf(array, value, 0, array?.Length ?? 0);

        public static int IndexOf<T>(T[] array, T value, int startIndex) =>
            IndexOf(array, value, startIndex, (array?.Length ?? 0) - startIndex);

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateSearchRange(array, startIndex, count);
            var comparer = EqualityComparer<T>.Default;
            var end = startIndex + count;
            for (var index = startIndex; index < end; index++)
            {
                if (comparer.Equals(array[index], value))
                {
                    return index;
                }
            }
            return -1;
        }

        public static int IndexOf(Array array, object? value) =>
            IndexOf(array, value, 0, array?.Length ?? 0);

        public static int IndexOf(Array array, object? value, int startIndex) =>
            IndexOf(array, value, startIndex, (array?.Length ?? 0) - startIndex);

        public static int IndexOf(Array array, object? value, int startIndex, int count)
        {
            if (array is object[] values)
            {
                ValidateSearchRange(values, startIndex, count);
                for (var index = startIndex; index < startIndex + count; index++)
                {
                    if (object.Equals(values[index], value))
                    {
                        return index;
                    }
                }
                return -1;
            }

            throw new PlatformNotSupportedException();
        }

        public static int LastIndexOf<T>(T[] array, T value) =>
            LastIndexOf(array, value, (array?.Length ?? 0) - 1, array?.Length ?? 0);

        public static int LastIndexOf<T>(T[] array, T value, int startIndex) =>
            LastIndexOf(array, value, startIndex, startIndex + 1);

        public static int LastIndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateLastSearchRange(array, startIndex, count);
            var comparer = EqualityComparer<T>.Default;
            var end = startIndex - count;
            for (var index = startIndex; index > end; index--)
            {
                if (comparer.Equals(array[index], value))
                {
                    return index;
                }
            }
            return -1;
        }

        public static int LastIndexOf(Array array, object? value) =>
            LastIndexOf(array, value, (array?.Length ?? 0) - 1, array?.Length ?? 0);

        public static int LastIndexOf(Array array, object? value, int startIndex) =>
            LastIndexOf(array, value, startIndex, startIndex + 1);

        public static int LastIndexOf(Array array, object? value, int startIndex, int count)
        {
            if (array is object[] values)
            {
                ValidateLastSearchRange(values, startIndex, count);
                for (var index = startIndex; index > startIndex - count; index--)
                {
                    if (object.Equals(values[index], value))
                    {
                        return index;
                    }
                }
                return -1;
            }

            throw new PlatformNotSupportedException();
        }

        public static int BinarySearch<T>(T[] array, T value) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, null);

        public static int BinarySearch<T>(T[] array, T value, IComparer<T>? comparer) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, comparer);

        public static int BinarySearch<T>(T[] array, int index, int length, T value) =>
            BinarySearch(array, index, length, value, null);

        public static int BinarySearch<T>(
            T[] array,
            int index,
            int length,
            T value,
            IComparer<T>? comparer)
        {
            ValidateRange(array, index, length);
            comparer ??= Comparer<T>.Default;
            var low = index;
            var high = index + length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var comparison = comparer.Compare(array[middle], value);
                if (comparison == 0)
                {
                    return middle;
                }
                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return ~low;
        }

        public static int BinarySearch(Array array, object? value) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, null);

        public static int BinarySearch(Array array, object? value, IComparer? comparer) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, comparer);

        public static int BinarySearch(Array array, int index, int length, object? value) =>
            BinarySearch(array, index, length, value, null);

        public static int BinarySearch(
            Array array,
            int index,
            int length,
            object? value,
            IComparer? comparer)
        {
            if (array is object[] values)
            {
                ValidateRange(values, index, length);
                comparer ??= DefaultArrayComparer.Instance;
                var low = index;
                var high = index + length - 1;
                while (low <= high)
                {
                    var middle = low + ((high - low) >> 1);
                    var comparison = comparer.Compare(values[middle], value);
                    if (comparison == 0) return middle;
                    if (comparison < 0) low = middle + 1;
                    else high = middle - 1;
                }
                return ~low;
            }

            throw new PlatformNotSupportedException();
        }

        public static void ConstrainedCopy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            if (sourceArray is object[] source && destinationArray is object[] destination)
            {
                ValidateRange(source, sourceIndex, length);
                ValidateRange(destination, destinationIndex, length);
                var copy = new object[length];
                Copy(source, sourceIndex, copy, 0, length);
                Copy(copy, 0, destination, destinationIndex, length);
                return;
            }

            throw new PlatformNotSupportedException();
        }

        public static TOutput[] ConvertAll<TInput, TOutput>(
            TInput[] array,
            Converter<TInput, TOutput> converter)
        {
            if (array is null || converter is null)
            {
                throw new ArgumentNullException();
            }
            var result = new TOutput[array.Length];
            for (var index = 0; index < array.Length; index++)
            {
                result[index] = converter(array[index]);
            }
            return result;
        }

        public static bool Exists<T>(T[] array, Predicate<T> match) =>
            FindIndex(array, match) >= 0;

        public static void Fill<T>(T[] array, T value) =>
            Fill(array, value, 0, array?.Length ?? 0);

        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateRange(array, startIndex, count);
            for (var index = startIndex; index < startIndex + count; index++)
            {
                array[index] = value;
            }
        }

        public static T? Find<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            for (var index = 0; index < array.Length; index++)
            {
                if (match(array[index])) return array[index];
            }
            return default;
        }

        public static T[] FindAll<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            var result = new List<T>();
            for (var index = 0; index < array.Length; index++)
            {
                if (match(array[index])) result.Add(array[index]);
            }
            var values = new T[result.Count];
            result.CopyTo(values, 0);
            return values;
        }

        public static int FindIndex<T>(T[] array, Predicate<T> match) =>
            FindIndex(array, 0, array?.Length ?? 0, match);

        public static int FindIndex<T>(T[] array, int startIndex, Predicate<T> match) =>
            FindIndex(array, startIndex, (array?.Length ?? 0) - startIndex, match);

        public static int FindIndex<T>(T[] array, int startIndex, int count, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            ValidateRange(array, startIndex, count);
            for (var index = startIndex; index < startIndex + count; index++)
            {
                if (match(array[index])) return index;
            }
            return -1;
        }

        public static T? FindLast<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            for (var index = array.Length - 1; index >= 0; index--)
            {
                if (match(array[index])) return array[index];
            }
            return default;
        }

        public static int FindLastIndex<T>(T[] array, Predicate<T> match) =>
            FindLastIndex(array, (array?.Length ?? 0) - 1, array?.Length ?? 0, match);

        public static int FindLastIndex<T>(T[] array, int startIndex, Predicate<T> match) =>
            FindLastIndex(array, startIndex, startIndex + 1, match);

        public static int FindLastIndex<T>(T[] array, int startIndex, int count, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            ValidateLastSearchRange(array, startIndex, count);
            for (var index = startIndex; index > startIndex - count; index--)
            {
                if (match(array[index])) return index;
            }
            return -1;
        }

        public static void ForEach<T>(T[] array, Action<T> action)
        {
            if (array is null || action is null)
            {
                throw new ArgumentNullException();
            }
            for (var index = 0; index < array.Length; index++) action(array[index]);
        }

        public static void Reverse<T>(T[] array) =>
            Reverse(array, 0, array?.Length ?? 0);

        public static void Reverse<T>(T[] array, int index, int length)
        {
            ValidateRange(array, index, length);
            var left = index;
            var right = index + length - 1;
            while (left < right)
            {
                (array[left], array[right]) = (array[right], array[left]);
                left++;
                right--;
            }
        }

        public static void Reverse(Array array)
        {
            Reverse(array, 0, array?.Length ?? 0);
        }

        public static void Reverse(Array array, int index, int length)
        {
            if (array is object[] values)
            {
                Reverse(values, index, length);
                return;
            }

            throw new PlatformNotSupportedException();
        }

        public static void Sort<T>(T[] array) => Sort(array, 0, array?.Length ?? 0, null);
        public static void Sort<T>(T[] array, IComparer<T>? comparer) =>
            Sort(array, 0, array?.Length ?? 0, comparer);
        public static void Sort<T>(T[] array, int index, int length) =>
            Sort(array, index, length, null);
        public static void Sort<T>(T[] array, Comparison<T> comparison)
        {
            if (comparison is null) throw new ArgumentNullException();
            Sort(array, 0, array?.Length ?? 0, new ComparisonComparer<T>(comparison));
        }
        public static void Sort<T>(T[] array, int index, int length, IComparer<T>? comparer)
        {
            ValidateRange(array, index, length);
            comparer ??= Comparer<T>.Default;
            for (var current = index + 1; current < index + length; current++)
            {
                var value = array[current];
                var insertion = current;
                while (insertion > index && comparer.Compare(array[insertion - 1], value) > 0)
                {
                    array[insertion] = array[insertion - 1];
                    insertion--;
                }
                array[insertion] = value;
            }
        }

        public static void Sort<TKey, TValue>(TKey[] keys, TValue[]? items) =>
            Sort(keys, items, 0, keys?.Length ?? 0, null);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[]? items, IComparer<TKey>? comparer) =>
            Sort(keys, items, 0, keys?.Length ?? 0, comparer);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[]? items, int index, int length) =>
            Sort(keys, items, index, length, null);
        public static void Sort<TKey, TValue>(
            TKey[] keys,
            TValue[]? items,
            int index,
            int length,
            IComparer<TKey>? comparer)
        {
            ValidateRange(keys, index, length);
            if (items is not null) ValidateRange(items, index, length);
            comparer ??= Comparer<TKey>.Default;
            for (var current = index + 1; current < index + length; current++)
            {
                var key = keys[current];
                var item = items is null ? default : items[current];
                var insertion = current;
                while (insertion > index && comparer.Compare(keys[insertion - 1], key) > 0)
                {
                    keys[insertion] = keys[insertion - 1];
                    if (items is not null) items[insertion] = items[insertion - 1];
                    insertion--;
                }
                keys[insertion] = key;
                if (items is not null) items[insertion] = item!;
            }
        }

        public static void Sort(Array array) => Sort(array, 0, array?.Length ?? 0, null);
        public static void Sort(Array array, IComparer? comparer) =>
            Sort(array, 0, array?.Length ?? 0, comparer);
        public static void Sort(Array array, int index, int length) =>
            Sort(array, index, length, null);
        public static void Sort(Array array, int index, int length, IComparer? comparer)
        {
            if (array is object[] values)
            {
                ValidateRange(values, index, length);
                comparer ??= DefaultArrayComparer.Instance;
                for (var current = index + 1; current < index + length; current++)
                {
                    var value = values[current];
                    var insertion = current;
                    while (insertion > index && comparer.Compare(values[insertion - 1], value) > 0)
                    {
                        values[insertion] = values[insertion - 1];
                        insertion--;
                    }
                    values[insertion] = value;
                }
                return;
            }

            throw new PlatformNotSupportedException();
        }

        public static void Sort(Array keys, Array? items)
        {
            Sort(keys, items, 0, keys?.Length ?? 0, null);
        }

        public static void Sort(Array keys, Array? items, IComparer? comparer)
        {
            Sort(keys, items, 0, keys?.Length ?? 0, comparer);
        }

        public static void Sort(Array keys, Array? items, int index, int length)
        {
            Sort(keys, items, index, length, null);
        }

        public static void Sort(
            Array keys,
            Array? items,
            int index,
            int length,
            IComparer? comparer)
        {
            if (keys is object[] keyValues && (items is null || items is object[]))
            {
                var itemValues = (object[]?)items;
                ValidateRange(keyValues, index, length);
                if (itemValues is not null) ValidateRange(itemValues, index, length);
                comparer ??= DefaultArrayComparer.Instance;
                for (var current = index + 1; current < index + length; current++)
                {
                    var key = keyValues[current];
                    var item = itemValues?[current];
                    var insertion = current;
                    while (insertion > index && comparer.Compare(keyValues[insertion - 1], key) > 0)
                    {
                        keyValues[insertion] = keyValues[insertion - 1];
                        if (itemValues is not null) itemValues[insertion] = itemValues[insertion - 1];
                        insertion--;
                    }
                    keyValues[insertion] = key;
                    if (itemValues is not null) itemValues[insertion] = item!;
                }
                return;
            }

            throw new PlatformNotSupportedException();
        }

        public static bool TrueForAll<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            for (var index = 0; index < array.Length; index++)
            {
                if (!match(array[index])) return false;
            }
            return true;
        }

        public int GetLength(int dimension)
        {
            if (dimension != 0) throw new IndexOutOfRangeException();
            return Length;
        }

        public long GetLongLength(int dimension) => GetLength(dimension);
        public int GetLowerBound(int dimension)
        {
            _ = GetLength(dimension);
            return 0;
        }
        public int GetUpperBound(int dimension)
        {
            return GetLength(dimension) - 1;
        }

        public object? GetValue(int index)
        {
            if (this is object[] values) return values[index];
            throw new PlatformNotSupportedException();
        }

        public object? GetValue(params int[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            return GetValue(indices[0]);
        }

        public object? GetValue(long index) => GetValue(ToInt32(index));

        public object? GetValue(params long[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            return GetValue(indices[0]);
        }

        public void SetValue(object? value, int index)
        {
            if (this is object[] values)
            {
                values[index] = value!;
                return;
            }
            throw new PlatformNotSupportedException();
        }

        public void SetValue(object? value, params int[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            SetValue(value, indices[0]);
        }

        public void SetValue(object? value, long index) => SetValue(value, ToInt32(index));

        public void SetValue(object? value, params long[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            SetValue(value, indices[0]);
        }

        public void CopyTo(Array array, int index)
        {
            Copy(this, 0, array, index, Length);
        }

        public void CopyTo(Array array, long index) => CopyTo(array, ToInt32(index));

        public object Clone()
        {
            return InternalClone(this);
        }

        private static object InternalClone(Array array) =>
            throw new PlatformNotSupportedException();

        public Collections.IEnumerator GetEnumerator()
        {
            if (this is object[] values) return new SZArrayEnumerator<object>(values);
            throw new PlatformNotSupportedException();
        }

        public void Initialize()
        {
        }

        int ICollection.Count => Length;
        void ICollection.CopyTo(Array array, int index) => CopyTo(array, index);

        object? IList.this[int index]
        {
            get => GetValue(index);
            set => SetValue(value, index);
        }

        int IList.Add(object? value) => throw new NotSupportedException();
        bool IList.Contains(object? value) => IndexOf(this, value) >= 0;
        void IList.Clear() => Clear(this);
        int IList.IndexOf(object? value) => IndexOf(this, value);
        void IList.Insert(int index, object? value) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
        void IList.RemoveAt(int index) => throw new NotSupportedException();

        int IStructuralComparable.CompareTo(object? other, IComparer comparer)
        {
            if (other is null) return 1;
            if (other is not Array values || values.Length != Length) throw new ArgumentException();
            for (var index = 0; index < Length; index++)
            {
                var result = comparer.Compare(GetValue(index), values.GetValue(index));
                if (result != 0) return result;
            }
            return 0;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is not Array values || values.Length != Length) return false;
            for (var index = 0; index < Length; index++)
            {
                if (!comparer.Equals(GetValue(index), values.GetValue(index))) return false;
            }
            return true;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
        {
            var hash = 17;
            var start = Length < 8 ? 0 : Length - 8;
            for (var index = start; index < Length; index++)
            {
                hash = hash * 31 + comparer.GetHashCode(GetValue(index)!);
            }
            return hash;
        }

        private static void ValidateRange<T>(T[]? array, int index, int length)
        {
            if (array is null) throw new ArgumentNullException();
            if (index < 0 || length < 0 || index > array.Length - length)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void ValidateRange(int arrayLength, int index, int length)
        {
            if (index < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (index > arrayLength - length) throw new ArgumentException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalCopy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void InternalClear(Array array, int index, int length);

        private static void ValidateSearchRange<T>(T[]? array, int startIndex, int count)
        {
            ValidateRange(array, startIndex, count);
        }

        private static void ValidateLastSearchRange<T>(T[]? array, int startIndex, int count)
        {
            if (array is null) throw new ArgumentNullException();
            if (array.Length == 0)
            {
                if (startIndex == -1 && count == 0) return;
                throw new ArgumentOutOfRangeException();
            }
            if (startIndex < 0 || startIndex >= array.Length || count < 0 || count > startIndex + 1)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void ValidatePredicate<T>(T[]? array, Predicate<T>? match)
        {
            if (array is null || match is null) throw new ArgumentNullException();
        }

        private static int ToInt32(long value)
        {
            var result = (int)value;
            return value == result ? result : throw new ArgumentOutOfRangeException();
        }

        private sealed class ComparisonComparer<T>(Comparison<T> comparison) : Comparer<T>
        {
            public override int Compare(T? left, T? right) => comparison(left!, right!);
        }

        private sealed class DefaultArrayComparer : IComparer
        {
            internal static DefaultArrayComparer Instance { get; } = new();

            public int Compare(object? left, object? right)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left is null) return -1;
                if (right is null) return 1;
                if (left is IComparable comparable) return comparable.CompareTo(right);
                throw new ArgumentException("At least one object must implement IComparable.");
            }
        }
    }
}
