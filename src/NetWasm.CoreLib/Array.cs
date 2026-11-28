// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    // The compiler supplies the storage and indexing representation of managed
    // arrays. Rectangular-array construction and typed Get/Set/Address calls
    // are runtime-provided members lowered by the compiler; the managed
    // members below provide the shared System.Array surface.
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public abstract class Array : ICollection, IEnumerable, IList, IStructuralComparable,
        IStructuralEquatable, ICloneable
    {
        internal const int IntrosortSizeThreshold = 16;

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

        private static class EmptyArray<T>
        {
            internal static readonly T[] Value = new T[0];
        }

        public static T[] Empty<T>() => EmptyArray<T>.Value;

        public static ReadOnlyCollection<T> AsReadOnly<T>(T[] array)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }

            return new ReadOnlyCollection<T>(array);
        }

        public static void Clear(Array array)
        {
            ArgumentNullException.ThrowIfNull(array);
            InternalClear(array, 0, array.Length);
        }

        public static void Clear(Array array, int index, int length)
        {
            ArgumentNullException.ThrowIfNull(array);
            var offset = (long)index - array.GetLowerBound(0);
            if (offset < 0 || offset > array.Length || length < 0 || length > array.Length - offset)
                throw new IndexOutOfRangeException();
            InternalClear(array, (int)offset, length);
        }

        // The first element is always at physical offset zero, including ARRAY instances whose
        // public indices start at a non-zero bound.
        public static void Copy(Array sourceArray, Array destinationArray, int length) =>
            CopyPhysical<PrimitiveElementConversion>(sourceArray, 0, destinationArray, 0, length);

        public static void Copy(Array sourceArray, Array destinationArray, long length)
        {
            Copy(sourceArray, destinationArray, ToInt32(length));
        }

        public static void Copy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length) =>
            CopyIndexed<PrimitiveElementConversion>(sourceArray, sourceIndex, destinationArray, destinationIndex, length);

        // The compiler calls these for Copy whose two arrays have one static one-dimensional
        // type (ArrayCopyCallRewriter). Their elements are then one type, or types the runtime
        // treats as one (int and uint, an enum and its underlying type), so they never convert
        // and a program that copies only such arrays carries no conversion.
        internal static void CopyOfOneType(Array sourceArray, Array destinationArray, int length) =>
            CopyOfOneType(sourceArray, 0, destinationArray, 0, length);

        internal static void CopyOfOneType(Array sourceArray, Array destinationArray, long length) =>
            CopyOfOneType(sourceArray, destinationArray, ToInt32(length));

        // Both arrays are one-dimensional by their static types, so each starts at index 0 and has rank 1: upstream's fast
        // path (one method table, not multidimensional) checks only the range against the lengths, then copies; the
        // runtime's copy still checks each element it assigns. Anything out of range takes the full checks, which throw
        // as upstream's do.
        internal static void CopyOfOneType(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            if (sourceArray is not null && destinationArray is not null && length >= 0 && sourceIndex >= 0 &&
                destinationIndex >= 0 && (uint)(sourceIndex + length) <= (uint)sourceArray.Length &&
                (uint)(destinationIndex + length) <= (uint)destinationArray.Length)
            {
                CopyValidated<NoElementConversion>(sourceArray, sourceIndex, destinationArray, destinationIndex, length);
                return;
            }
            // A null array reaches the full checks, which throw ArgumentNullException as upstream's do.
            CopyIndexed<NoElementConversion>(sourceArray!, sourceIndex, destinationArray!, destinationIndex, length);
        }

        internal static void CopyOfOneType(
            Array sourceArray,
            long sourceIndex,
            Array destinationArray,
            long destinationIndex,
            long length) =>
            CopyOfOneType(sourceArray, ToInt32(sourceIndex), destinationArray, ToInt32(destinationIndex), ToInt32(length));

        private static void CopyIndexed<TConversion>(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
            where TConversion : struct, IElementConversion
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            var sourceOffset = (long)sourceIndex - sourceArray.GetLowerBound(0);
            var destinationOffset = (long)destinationIndex - destinationArray.GetLowerBound(0);
            ValidateCopy(sourceArray, sourceOffset, destinationArray, destinationOffset, length);
            CopyPhysical<TConversion>(sourceArray, (int)sourceOffset, destinationArray, (int)destinationOffset, length);
        }

        // Strategy: what a copy does with elements the runtime cannot assign to the destination's,
        // chosen by each caller at compile time so that only a copy that may convert reaches the
        // conversion.
        private interface IElementConversion
        {
            static abstract bool TryCopy(
                Array sourceArray,
                int sourceIndex,
                Array destinationArray,
                int destinationIndex,
                int length);
        }

        private struct NoElementConversion : IElementConversion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryCopy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length) =>
                false;
        }

        private struct PrimitiveElementConversion : IElementConversion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryCopy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length) =>
                TryCopyPrimitives(sourceArray, sourceIndex, destinationArray, destinationIndex, length);
        }

        private static void CopyPhysical<TConversion>(
            Array sourceArray,
            int sourceOffset,
            Array destinationArray,
            int destinationOffset,
            int length)
            where TConversion : struct, IElementConversion
        {
            ValidateCopy(sourceArray, sourceOffset, destinationArray, destinationOffset, length);
            CopyValidated<TConversion>(sourceArray, sourceOffset, destinationArray, destinationOffset, length);
        }

        // The copy once its arguments are known good: the runtime copies what it can assign, and the conversion takes
        // what it cannot.
        private static void CopyValidated<TConversion>(
            Array sourceArray,
            int sourceOffset,
            Array destinationArray,
            int destinationOffset,
            int length)
            where TConversion : struct, IElementConversion
        {
            var status = InternalCopy(
                sourceArray,
                sourceOffset,
                destinationArray,
                destinationOffset,
                length);
            if (status == ArrayCopyStatus.TypeMismatch)
            {
                if (TConversion.TryCopy(sourceArray, sourceOffset, destinationArray, destinationOffset, length))
                    return;
                throw new ArrayTypeMismatchException();
            }
            if (status == ArrayCopyStatus.ElementCastFailure)
            {
                throw new InvalidCastException();
            }
            if (status == ArrayCopyStatus.OutOfMemory)
            {
                throw new OutOfMemoryException();
            }
            if (status != ArrayCopyStatus.Success)
            {
                throw new InvalidOperationException();
            }
        }

        // The runtime's copy takes arrays whose element types it can assign; upstream's
        // CanAssignArrayType (Array.CoreCLR.cs) also takes primitive elements (an enum's by its
        // underlying type) that are the same integral type up to sign, which it copies as they
        // are, or that widen, which CopyImplPrimitiveWiden converts one at a time.
        private static bool TryCopyPrimitives(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            CorElementType srcElType = GetPrimitiveElementType(sourceArray);
            CorElementType destElType = GetPrimitiveElementType(destinationArray);
            if (!srcElType.IsPrimitiveType() || !destElType.IsPrimitiveType())
                return false;
            bool sameType = GetNormalizedIntegralArrayElementType(srcElType) == GetNormalizedIntegralArrayElementType(destElType);
            if (!sameType && !RuntimeHelpers.CanPrimitiveWiden(srcElType, destElType))
                return false;

            nuint srcElSize = GetPrimitiveSize(srcElType);
            nuint destElSize = GetPrimitiveSize(destElType);
            ref byte srcData = ref Unsafe.AddByteOffset(ref MemoryMarshal.GetArrayDataReference(sourceArray), (nuint)sourceIndex * srcElSize);
            ref byte data = ref Unsafe.AddByteOffset(ref MemoryMarshal.GetArrayDataReference(destinationArray), (nuint)destinationIndex * destElSize);
            if (sameType)
            {
                SpanHelpers.Memmove(ref data, ref srcData, (nuint)length * srcElSize);
                return true;
            }

            for (int i = 0; i < length; i++)
            {
                InvokeUtils.PrimitiveWiden(ref srcData, ref data, srcElType, destElType);
                srcData = ref Unsafe.AddByteOffset(ref srcData, srcElSize);
                data = ref Unsafe.AddByteOffset(ref data, destElSize);
            }
            return true;
        }

        // Upstream's, from Array.CoreCLR.cs at deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
        private static CorElementType GetNormalizedIntegralArrayElementType(CorElementType elementType)
        {
            Debug.Assert(elementType.IsPrimitiveType());

            // Array Primitive types such as E_T_I4 and E_T_U4 are interchangeable
            // Enums with interchangeable underlying types are interchangeable
            // BOOL is NOT interchangeable with I1/U1, neither CHAR -- with I2/U2

            // U1/U2/U4/U8/U
            int shift = (0b0010_0000_0000_0000_1010_1010_0000 >> (int)elementType) & 1;
            return (CorElementType)((int)elementType - shift);
        }

        private static nuint GetPrimitiveSize(CorElementType elementType) => elementType switch
        {
            CorElementType.ELEMENT_TYPE_BOOLEAN or CorElementType.ELEMENT_TYPE_I1 or CorElementType.ELEMENT_TYPE_U1 => 1,
            CorElementType.ELEMENT_TYPE_CHAR or CorElementType.ELEMENT_TYPE_I2 or CorElementType.ELEMENT_TYPE_U2 => 2,
            CorElementType.ELEMENT_TYPE_I4 or CorElementType.ELEMENT_TYPE_U4 or CorElementType.ELEMENT_TYPE_R4 => 4,
            CorElementType.ELEMENT_TYPE_I8 or CorElementType.ELEMENT_TYPE_U8 or CorElementType.ELEMENT_TYPE_R8 => 8,
            _ => (nuint)IntPtr.Size,
        };

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

        // Upstream's, from Array.cs at deae2e32bf0e018e09d64597b7c59b62b73bb3ce: an array already of the size stays as it is,
        // and the elements move with Buffer.Memmove rather than through Array.Copy's checks.
        public static void Resize<T>([NotNull] ref T[]? array, int newSize)
        {
            if (newSize < 0)
            {
                ThrowArgumentOutOfRange("newSize", SR.ArgumentOutOfRange_NeedNonNegNum);
            }

            T[]? larray = array; // local copy
            if (larray == null)
            {
                array = new T[newSize];
                return;
            }

            if (larray.Length != newSize)
            {
                // Due to array variance, it's possible that the incoming array is
                // actually of type U[], where U:T; or that an int[] <-> uint[] or
                // similar cast has occurred. In any case, since it's always legal
                // to reinterpret U as T in this scenario (but not necessarily the
                // other way around), we can use Buffer.Memmove here.

                T[] newArray = new T[newSize];
                Buffer.Memmove(
                    ref MemoryMarshal.GetArrayDataReference(newArray),
                    ref MemoryMarshal.GetArrayDataReference(larray),
                    (uint)Math.Min(newSize, larray.Length));
                array = newArray;
            }

            Debug.Assert(array != null);
        }

        public static int IndexOf<T>(T[] array, T value) =>
            IndexOf(array, value, 0, array?.Length ?? 0);

        public static int IndexOf<T>(T[] array, T value, int startIndex) =>
            IndexOf(array, value, startIndex, (array?.Length ?? 0) - startIndex);

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if ((uint)startIndex > (uint)array.Length)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }
            if ((uint)count > (uint)(array.Length - startIndex))
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
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

        public static int IndexOf(Array array, object? value)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return IndexOf(array, value, array.GetLowerBound(0), array.Length);
        }

        public static int IndexOf(Array array, object? value, int startIndex)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return IndexOf(array, value, startIndex, array.Length - startIndex + array.GetLowerBound(0));
        }

        public static int IndexOf(Array array, object? value, int startIndex, int count)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (array.Rank != 1)
            {
                ThrowRank(SR.Rank_MultiDimNotSupported);
            }
            var lowerBound = array.GetLowerBound(0);
            if (startIndex < lowerBound || startIndex > array.Length + lowerBound)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }
            if (count < 0 || count > array.Length - startIndex + lowerBound)
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
            var physicalIndex = startIndex - lowerBound;
            for (var offset = 0; offset < count; offset++)
            {
                if (object.Equals(
                        InternalGetValue(array, physicalIndex + offset),
                        value))
                {
                    return startIndex + offset;
                }
            }

            return lowerBound - 1;
        }

        public static int LastIndexOf<T>(T[] array, T value) =>
            LastIndexOf(array, value, (array?.Length ?? 0) - 1, array?.Length ?? 0);

        public static int LastIndexOf<T>(T[] array, T value, int startIndex)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return LastIndexOf(array, value, startIndex, array.Length == 0 ? 0 : startIndex + 1);
        }

        public static int LastIndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateLastSearchRange(array, startIndex, count);
            if (array.Length == 0)
            {
                return -1;
            }
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

        public static int LastIndexOf(Array array, object? value)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return LastIndexOf(array, value, array.Length - 1 + array.GetLowerBound(0), array.Length);
        }

        public static int LastIndexOf(Array array, object? value, int startIndex)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return LastIndexOf(array, value, startIndex, startIndex + 1 - array.GetLowerBound(0));
        }

        public static int LastIndexOf(Array array, object? value, int startIndex, int count)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            var lowerBound = array.GetLowerBound(0);
            if (array.Length == 0)
            {
                return lowerBound - 1;
            }
            if (startIndex < lowerBound || startIndex >= array.Length + lowerBound)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLess);
            }
            if (count < 0)
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
            if (count > startIndex - lowerBound + 1)
            {
                ThrowArgumentOutOfRange("endIndex", SR.ArgumentOutOfRange_EndIndexStartIndex);
            }
            if (array.Rank != 1)
            {
                ThrowRank(SR.Rank_MultiDimNotSupported);
            }

            var physicalIndex = startIndex - lowerBound;
            for (var offset = 0; offset < count; offset++)
            {
                if (object.Equals(
                        InternalGetValue(array, physicalIndex - offset),
                        value))
                {
                    return startIndex - offset;
                }
            }

            return lowerBound - 1;
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
            ValidateIndexLength(array, index, length);
            return ArraySortHelper<T>.Default.BinarySearch(
                array,
                index,
                length,
                value,
                comparer);
        }

        public static int BinarySearch(Array array, object? value) => BinarySearch(array, value, null);

        public static int BinarySearch(Array array, object? value, IComparer? comparer)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return BinarySearch(array, array.GetLowerBound(0), array.Length, value, comparer);
        }

        public static int BinarySearch(Array array, int index, int length, object? value) =>
            BinarySearch(array, index, length, value, null);

        public static int BinarySearch(
            Array array,
            int index,
            int length,
            object? value,
            IComparer? comparer)
        {
            var lowerBound = ValidateVectorIndexLength(array, index, length);
            comparer ??= DefaultArrayComparer.Instance;
            var low = index;
            var high = index + length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                int comparison;
                try
                {
                    comparison = comparer.Compare(
                        InternalGetValue(array, middle - lowerBound),
                        value);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(null, exception);
                }
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }

            return ~low;
        }

        public static void ConstrainedCopy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            var sourceOffset = (long)sourceIndex - sourceArray.GetLowerBound(0);
            var destinationOffset = (long)destinationIndex - destinationArray.GetLowerBound(0);
            ValidateCopy(sourceArray, sourceOffset, destinationArray, destinationOffset, length);
            if (!destinationArray.GetType().IsAssignableFrom(sourceArray.GetType()))
            {
                throw new ArrayTypeMismatchException();
            }

            CopyPhysical<NoElementConversion>(
                sourceArray,
                (int)sourceOffset,
                destinationArray,
                (int)destinationOffset,
                length);
        }

        public static TOutput[] ConvertAll<TInput, TOutput>(
            TInput[] array,
            Converter<TInput, TOutput> converter)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (converter is null)
            {
                ThrowArgumentNull("converter");
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
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if ((uint)startIndex > (uint)array.Length)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }
            if ((uint)count > (uint)(array.Length - startIndex))
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
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
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (startIndex < 0 || startIndex > array.Length)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);
            }
            if (count < 0 || startIndex > array.Length - count)
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
            if (match is null)
            {
                ThrowArgumentNull("match");
            }
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

        public static int FindLastIndex<T>(T[] array, int startIndex, Predicate<T> match)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            return FindLastIndex(array, startIndex, array.Length == 0 ? 0 : startIndex + 1, match);
        }

        public static int FindLastIndex<T>(T[] array, int startIndex, int count, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            if (array.Length == 0)
            {
                if (startIndex != -1)
                {
                    ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLess);
                }
            }
            else if (startIndex < 0 || startIndex >= array.Length)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLess);
            }
            if (count < 0 || startIndex - count + 1 < 0)
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
            for (var index = startIndex; index > startIndex - count; index--)
            {
                if (match(array[index])) return index;
            }
            return -1;
        }

        public static void ForEach<T>(T[] array, Action<T> action)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (action is null)
            {
                ThrowArgumentNull("action");
            }
            for (var index = 0; index < array.Length; index++) action(array[index]);
        }

        public static void Reverse<T>(T[] array) =>
            Reverse(array, 0, array?.Length ?? 0);

        public static void Reverse<T>(T[] array, int index, int length)
        {
            ValidateIndexLength(array, index, length);
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
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            Reverse(array, array.GetLowerBound(0), array.Length);
        }

        public static void Reverse(Array array, int index, int length)
        {
            var lowerBound = ValidateVectorIndexLength(array, index, length);
            if (length <= 1)
            {
                return;
            }

            var source = (Array)InternalClone(array);
            var physicalIndex = index - lowerBound;
            for (var offset = 0; offset < length; offset++)
            {
                CopyPhysical<NoElementConversion>(
                    source,
                    physicalIndex + length - offset - 1,
                    array,
                    physicalIndex + offset,
                    1);
            }
        }

        public static void Sort<T>(T[] array) => Sort(array, 0, array?.Length ?? 0, null);
        public static void Sort<T>(T[] array, IComparer<T>? comparer) =>
            Sort(array, 0, array?.Length ?? 0, comparer);
        public static void Sort<T>(T[] array, int index, int length) =>
            Sort(array, index, length, null);
        public static void Sort<T>(T[] array, Comparison<T> comparison)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (comparison is null)
            {
                ThrowArgumentNull("comparison");
            }
            ArraySortHelper<T>.Sort(SortSpan(array, 0, array.Length), comparison);
        }
        public static void Sort<T>(T[] array, int index, int length, IComparer<T>? comparer)
        {
            ValidateIndexLength(array, index, length);
            ArraySortHelper<T>.Default.Sort(SortSpan(array, index, length), comparer);
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
            if (keys is null)
            {
                ThrowArgumentNull("keys");
            }
            if (index < 0)
            {
                ThrowArgumentOutOfRange("index", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (length < 0)
            {
                ThrowArgumentOutOfRange("length", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (keys.Length - index < length || (items is not null && index > items.Length - length))
            {
                ThrowArgument(SR.Argument_InvalidOffLen, null);
            }
            if (items is null)
            {
                ArraySortHelper<TKey>.Default.Sort(
                    SortSpan(keys, index, length),
                    comparer);
                return;
            }
            ArraySortHelper<TKey, TValue>.Default.Sort(
                SortSpan(keys, index, length),
                SortSpan(items, index, length),
                comparer);
        }

        // Sorting only permutes an array's own elements, so a covariant array (a string[] passed as an object[])
        // sorts through a span of its static element type, as upstream does; new Span<T>(T[]) rejects such arrays.
        private static Span<T> SortSpan<T>(T[] array, int index, int length) =>
            new(ref Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array), (nint)(uint)index), length);

        public static void Sort(Array array) => Sort(array, (IComparer?)null);
        public static void Sort(Array array, IComparer? comparer)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            Sort(array, null, array.GetLowerBound(0), array.Length, comparer);
        }
        public static void Sort(Array array, int index, int length) =>
            Sort(array, index, length, null);
        public static void Sort(Array array, int index, int length, IComparer? comparer)
        {
            Sort(array, null, index, length, comparer);
        }

        public static void Sort(Array keys, Array? items)
        {
            Sort(keys, items, (IComparer?)null);
        }

        public static void Sort(Array keys, Array? items, IComparer? comparer)
        {
            if (keys is null)
            {
                ThrowArgumentNull("keys");
            }
            Sort(keys, items, keys.GetLowerBound(0), keys.Length, comparer);
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
            if (keys is null)
            {
                ThrowArgumentNull("keys");
            }
            if (keys.Rank != 1 || (items is not null && items.Rank != 1))
            {
                ThrowRank(SR.Rank_MultiDimNotSupported);
            }
            var lowerBound = keys.GetLowerBound(0);
            if (items is not null && lowerBound != items.GetLowerBound(0))
            {
                ThrowArgument(SR.Arg_LowerBoundsMustMatch, null);
            }
            if (index < lowerBound)
            {
                ThrowArgumentOutOfRange("index", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (length < 0)
            {
                ThrowArgumentOutOfRange("length", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (keys.Length - (index - lowerBound) < length ||
                (items is not null && index - lowerBound > items.Length - length))
            {
                ThrowArgument(SR.Argument_InvalidOffLen, null);
            }
            if (length <= 1)
            {
                return;
            }

            comparer ??= DefaultArrayComparer.Instance;
            if (keys is object[] keyValues &&
                (items is null || items is object[]))
            {
                var physicalIndex = index - lowerBound;
                var itemValues = (object[]?)items;
                var adapter = new NonGenericComparerAdapter(comparer);
                if (itemValues is null)
                {
                    ArraySortHelper<object?>.Default.Sort(
                        SortSpan<object?>(keyValues, physicalIndex, length),
                        adapter);
                    return;
                }
                ArraySortHelper<object?, object?>.Default.Sort(
                    SortSpan<object?>(keyValues, physicalIndex, length),
                    SortSpan<object?>(itemValues, physicalIndex, length),
                    adapter);
                return;
            }

            var order = new int[length];
            for (var offset = 0; offset < length; offset++)
            {
                order[offset] = offset;
            }
            var sourceOffset = index - lowerBound;
            ArraySortHelper<int>.Default.Sort(
                new Span<int>(order),
                new ArrayIndexComparer(keys, sourceOffset, comparer));

            var keySource = (Array)InternalClone(keys);
            var itemSource = items is null ? null : (Array)InternalClone(items);
            for (var offset = 0; offset < length; offset++)
            {
                CopyPhysical<NoElementConversion>(
                    keySource,
                    sourceOffset + order[offset],
                    keys,
                    sourceOffset + offset,
                    1);
                if (items is not null)
                {
                    CopyPhysical<NoElementConversion>(
                        itemSource!,
                        sourceOffset + order[offset],
                        items,
                        sourceOffset + offset,
                        1);
                }
            }
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

        [Intrinsic]
        public int GetLength(int dimension)
        {
            if (dimension != 0) throw new IndexOutOfRangeException();
            return Length;
        }

        public long GetLongLength(int dimension) => GetLength(dimension);
        [Intrinsic]
        public int GetLowerBound(int dimension)
        {
            _ = GetLength(dimension);
            return 0;
        }
        [Intrinsic]
        public int GetUpperBound(int dimension)
        {
            return unchecked(GetLowerBound(dimension) + GetLength(dimension) - 1);
        }

        public object? GetValue(int index)
        {
            if (Rank != 1) throw new ArgumentException();
            var offset = (long)index - GetLowerBound(0);
            if (offset < 0 || offset >= Length) throw new IndexOutOfRangeException();
            return InternalGetValue(this, (int)offset);
        }

        public object? GetValue(int index1, int index2) =>
            GetValue(new[] { index1, index2 });

        public object? GetValue(int index1, int index2, int index3) =>
            GetValue(new[] { index1, index2, index3 });

        public object? GetValue(params int[] indices)
        {
            ArgumentNullException.ThrowIfNull(indices);
            if (indices.Length != Rank) throw new ArgumentException();
            return InternalGetValue(this, FlattenedOffset(indices));
        }

        public object? GetValue(long index) => GetValue(AssignmentIndex(index, nameof(index)));
        public object? GetValue(long index1, long index2) =>
            GetValue(AssignmentIndex(index1, nameof(index1)), AssignmentIndex(index2, nameof(index2)));
        public object? GetValue(long index1, long index2, long index3) =>
            GetValue(AssignmentIndex(index1, nameof(index1)), AssignmentIndex(index2, nameof(index2)),
                AssignmentIndex(index3, nameof(index3)));

        public object? GetValue(params long[] indices)
        {
            ArgumentNullException.ThrowIfNull(indices);
            if (indices.Length != Rank) throw new ArgumentException();
            var converted = new int[indices.Length];
            for (var index = 0; index < indices.Length; index++)
                converted[index] = AssignmentIndex(indices[index], "index");
            return GetValue(converted);
        }

        public void SetValue(object? value, int index)
        {
            if (Rank != 1) throw new ArgumentException();
            SetValueAtOffset(value, CheckedOffset(index, 0));
        }

        public void SetValue(object? value, int index1, int index2) =>
            SetValue(value, new[] { index1, index2 });

        public void SetValue(object? value, int index1, int index2, int index3) =>
            SetValue(value, new[] { index1, index2, index3 });

        public void SetValue(object? value, params int[] indices)
        {
            ArgumentNullException.ThrowIfNull(indices);
            if (indices.Length != Rank) throw new ArgumentException();
            SetValueAtOffset(value, FlattenedOffset(indices));
        }

        public void SetValue(object? value, long index) =>
            SetValue(value, AssignmentIndex(index, nameof(index)));
        public void SetValue(object? value, long index1, long index2) =>
            SetValue(value, AssignmentIndex(index1, nameof(index1)), AssignmentIndex(index2, nameof(index2)));
        public void SetValue(object? value, long index1, long index2, long index3) =>
            SetValue(value, AssignmentIndex(index1, nameof(index1)), AssignmentIndex(index2, nameof(index2)),
                AssignmentIndex(index3, nameof(index3)));

        public void SetValue(object? value, params long[] indices)
        {
            ArgumentNullException.ThrowIfNull(indices);
            if (indices.Length != Rank) throw new ArgumentException();
            var converted = new int[indices.Length];
            for (var index = 0; index < indices.Length; index++)
                converted[index] = AssignmentIndex(indices[index], "index");
            SetValue(value, converted);
        }

        private static int AssignmentIndex(long index, string parameter)
        {
            if (index < int.MinValue || index > int.MaxValue)
                throw new ArgumentOutOfRangeException(parameter);
            return (int)index;
        }

        // Row-major offset of indices whose count the caller has matched to Rank.
        private int FlattenedOffset(int[] indices)
        {
            var offset = 0;
            for (var dimension = 0; dimension < indices.Length; dimension++)
                offset = checked(offset * GetLength(dimension) + CheckedOffset(indices[dimension], dimension));
            return offset;
        }

        private int CheckedOffset(int index, int dimension)
        {
            var offset = (long)index - GetLowerBound(dimension);
            if (offset < 0 || offset >= GetLength(dimension)) throw new IndexOutOfRangeException();
            return (int)offset;
        }

        private void SetValueAtOffset(object? value, int offset)
        {
            if (value is not null)
                value = NormalizeAssignment(value, InternalElementType(this));
            if (InternalSetValue(this, offset, value) != 0) throw new InvalidCastException();
        }

        // Adapted from upstream's InternalSetValue (Array.CoreCLR.cs): the runtime stores a value of
        // the element's own type, so another enum or primitive value widens here to a true
        // primitive element, as RuntimeHelpers.CanPrimitiveWiden allows; a primitive never becomes
        // an enum.
        private static object NormalizeAssignment(object value, Type target)
        {
            var source = value.GetType();
            if (source == target) return value;
            var nullable = Nullable.GetUnderlyingType(target);
            if (nullable is not null)
            {
                if (source == nullable) return value;
                throw new InvalidCastException();
            }
            if (!target.IsValueType)
            {
                if (target.IsInstanceOfType(value)) return value;
                throw new InvalidCastException();
            }
            var sourceType = GetPrimitiveType(source);
            var targetType = target.IsEnum ? CorElementType.ELEMENT_TYPE_CLASS : GetPrimitiveType(target);
            if (!sourceType.IsPrimitiveType() || !targetType.IsPrimitiveType()) throw new InvalidCastException();
            if (!RuntimeHelpers.CanPrimitiveWiden(sourceType, targetType)) throw new ArgumentException();
            return Widen(value, sourceType, targetType);
        }

        // What upstream's GetCorElementTypeOfElementType (Array.CoreCLR.cs) answers: the array's
        // exact element type, an enum's being its underlying primitive and anything else
        // ELEMENT_TYPE_CLASS. A type test cannot give it, since int[] and uint[] pass each other's;
        // the compiler answers from the element types of the arrays the program constructs.
        internal static CorElementType GetPrimitiveElementType(Array array) =>
            (CorElementType)InternalPrimitiveElementType(array);

        private static int InternalPrimitiveElementType(Array array) => throw new PlatformNotSupportedException();

        // A primitive's element type, and an enum's underlying one, as upstream's
        // GetPrimitiveCorElementType reads them; any other type answers ELEMENT_TYPE_CLASS.
        private static CorElementType GetPrimitiveType(Type type) =>
            type == typeof(nint) ? CorElementType.ELEMENT_TYPE_I
            : type == typeof(nuint) ? CorElementType.ELEMENT_TYPE_U
            : Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => CorElementType.ELEMENT_TYPE_BOOLEAN,
                TypeCode.Char => CorElementType.ELEMENT_TYPE_CHAR,
                TypeCode.SByte => CorElementType.ELEMENT_TYPE_I1,
                TypeCode.Byte => CorElementType.ELEMENT_TYPE_U1,
                TypeCode.Int16 => CorElementType.ELEMENT_TYPE_I2,
                TypeCode.UInt16 => CorElementType.ELEMENT_TYPE_U2,
                TypeCode.Int32 => CorElementType.ELEMENT_TYPE_I4,
                TypeCode.UInt32 => CorElementType.ELEMENT_TYPE_U4,
                TypeCode.Int64 => CorElementType.ELEMENT_TYPE_I8,
                TypeCode.UInt64 => CorElementType.ELEMENT_TYPE_U8,
                TypeCode.Single => CorElementType.ELEMENT_TYPE_R4,
                TypeCode.Double => CorElementType.ELEMENT_TYPE_R8,
                _ => CorElementType.ELEMENT_TYPE_CLASS,
            };

        // Upstream's InvokeUtils.PrimitiveWiden for a value CanPrimitiveWiden accepts. A bool,
        // double, nint or nuint widens only to its own type, so it is already the target's.
        private static object Widen(object value, CorElementType source, CorElementType target)
        {
            if (source is CorElementType.ELEMENT_TYPE_BOOLEAN or CorElementType.ELEMENT_TYPE_R8 or
                CorElementType.ELEMENT_TYPE_I or CorElementType.ELEMENT_TYPE_U)
                return value;
            if (source == CorElementType.ELEMENT_TYPE_R4) return (double)(float)value;
            // Every other source is an integer or char, which fits each target it widens to.
            long integer = source switch
            {
                CorElementType.ELEMENT_TYPE_CHAR => (char)value,
                CorElementType.ELEMENT_TYPE_I1 => (sbyte)value,
                CorElementType.ELEMENT_TYPE_U1 => (byte)value,
                CorElementType.ELEMENT_TYPE_I2 => (short)value,
                CorElementType.ELEMENT_TYPE_U2 => (ushort)value,
                CorElementType.ELEMENT_TYPE_I4 => (int)value,
                CorElementType.ELEMENT_TYPE_U4 => (uint)value,
                CorElementType.ELEMENT_TYPE_I8 => (long)value,
                _ => unchecked((long)(ulong)value),
            };
            var unsigned = source == CorElementType.ELEMENT_TYPE_U8;
            // Each arm boxes its own type; without the casts the switch would convert them all to double.
            return target switch
            {
                CorElementType.ELEMENT_TYPE_CHAR => (object)(char)integer,
                CorElementType.ELEMENT_TYPE_I1 => (object)(sbyte)integer,
                CorElementType.ELEMENT_TYPE_U1 => (object)(byte)integer,
                CorElementType.ELEMENT_TYPE_I2 => (object)(short)integer,
                CorElementType.ELEMENT_TYPE_U2 => (object)(ushort)integer,
                CorElementType.ELEMENT_TYPE_I4 => (object)(int)integer,
                CorElementType.ELEMENT_TYPE_U4 => (object)(uint)integer,
                CorElementType.ELEMENT_TYPE_I8 => (object)integer,
                CorElementType.ELEMENT_TYPE_U8 => (object)unchecked((ulong)integer),
                CorElementType.ELEMENT_TYPE_R4 => (object)(unsigned ? (float)unchecked((ulong)integer) : (float)integer),
                _ => (object)(unsigned ? (double)unchecked((ulong)integer) : (double)integer),
            };
        }

        public static unsafe Array CreateInstance(Type elementType, int length)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            return InternalCreateInstance(elementType.SemanticTypeId, length) ??
                CreateSharedInstance(elementType, 1, &length, false) ??
                throw new NotSupportedException("The requested array element type is not available in this compiled program.");
        }

        public static Array CreateInstance(Type elementType, int length1, int length2)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentOutOfRangeException.ThrowIfNegative(length1);
            ArgumentOutOfRangeException.ThrowIfNegative(length2);
            return CreateRectangularInstance(elementType, new[] { length1, length2 });
        }

        public static Array CreateInstance(Type elementType, int length1, int length2, int length3)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentOutOfRangeException.ThrowIfNegative(length1);
            ArgumentOutOfRangeException.ThrowIfNegative(length2);
            ArgumentOutOfRangeException.ThrowIfNegative(length3);
            return CreateRectangularInstance(elementType, new[] { length1, length2, length3 });
        }

        public static Array CreateInstance(Type elementType, params int[] lengths)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentNullException.ThrowIfNull(lengths);
            if (lengths.Length == 0) throw new ArgumentException();
            ThrowIfNegativeLength(lengths);
            return CreateRectangularInstance(elementType, lengths);
        }

        public static Array CreateInstance(Type elementType, int[] lengths, int[] lowerBounds)
        {
            ArgumentNullException.ThrowIfNull(elementType);
            ArgumentNullException.ThrowIfNull(lengths);
            ArgumentNullException.ThrowIfNull(lowerBounds);
            if (lengths.Length != lowerBounds.Length) throw new ArgumentException();
            if (lengths.Length == 0) throw new ArgumentException();
            ThrowIfNegativeLength(lengths);
            return CreateBoundedInstance(elementType, lengths, lowerBounds);
        }

        public static Array CreateInstance(Type elementType, params long[] lengths)
        {
            ArgumentNullException.ThrowIfNull(lengths);
            var intLengths = new int[lengths.Length];
            for (var index = 0; index < lengths.Length; index++)
            {
                var length = lengths[index];
                var intLength = (int)length;
                if (length != intLength) throw new ArgumentOutOfRangeException("len");
                intLengths[index] = intLength;
            }
            return CreateInstance(elementType, intLengths);
        }

        // CreateInstanceFromArrayType as upstream validates it (Array.cs at 11.0.0); the array is the one CreateInstance
        // makes for the array type's element type and shape, which roots the array types a program can create (an
        // array type of rank one with zero lower bounds is a vector, as desktop makes it).
        public static Array CreateInstanceFromArrayType(Type arrayType, int length)
        {
            ArgumentNullException.ThrowIfNull(arrayType);
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            RuntimeType? t = arrayType as RuntimeType;
            if (t == null)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_MustBeType, ExceptionArgument.arrayType);

            if (!t.IsArray)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_HasToBeArrayClass, ExceptionArgument.arrayType);

            if (t.GetArrayRank() != 1)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported, ExceptionArgument.arrayType);

            return CreateInstance(t.GetElementType()!, length);
        }

        public static Array CreateInstanceFromArrayType(Type arrayType, params int[] lengths)
        {
            ArgumentNullException.ThrowIfNull(arrayType);
            ArgumentNullException.ThrowIfNull(lengths);

            RuntimeType? t = arrayType as RuntimeType;
            if (t == null)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_MustBeType, ExceptionArgument.arrayType);

            if (!t.IsArray)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_HasToBeArrayClass, ExceptionArgument.arrayType);

            if (t.GetArrayRank() != lengths.Length)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankIndices);

            for (int i = 0; i < lengths.Length; i++)
                if (lengths[i] < 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.lengths, i, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);

            return CreateRectangularInstance(t.GetElementType()!, lengths);
        }

        public static Array CreateInstanceFromArrayType(Type arrayType, int[] lengths, int[] lowerBounds)
        {
            ArgumentNullException.ThrowIfNull(arrayType);
            ArgumentNullException.ThrowIfNull(lengths);
            ArgumentNullException.ThrowIfNull(lowerBounds);

            if (lengths.Length != lowerBounds.Length)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RanksAndBounds);

            RuntimeType? t = arrayType as RuntimeType;
            if (t == null)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_MustBeType, ExceptionArgument.arrayType);

            if (!t.IsArray)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_HasToBeArrayClass, ExceptionArgument.arrayType);

            if (t.GetArrayRank() != lengths.Length)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankIndices);

            if (lowerBounds[0] != 0 && t.IsSZArray)
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);

            for (int i = 0; i < lengths.Length; i++)
                if (lengths[i] < 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.lengths, i, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);

            return CreateBoundedInstance(t.GetElementType()!, lengths, lowerBounds);
        }

        private static void ThrowIfNegativeLength(int[] lengths)
        {
            for (var index = 0; index < lengths.Length; index++)
                if (lengths[index] < 0) throw new ArgumentOutOfRangeException("lengths[" + index + "]");
        }

        // Copies the shape before allocation, as upstream does, so the caller cannot change it.
        // A rank-1 shape without a lower bound is a vector; any other shape is a rectangular
        // array type that the compiled program provides.
        private static unsafe Array CreateRectangularInstance(Type elementType, int[] lengths)
        {
            var rank = CheckedRank(lengths);
            var bounds = stackalloc int[rank * 2];
            CopyShape(lengths, null, bounds);
            if (rank == 1) return CreateInstance(elementType, bounds[0]);
            return InternalCreateRectangularInstance(elementType.SemanticTypeId, rank, bounds, false) ??
                CreateSharedInstance(elementType, rank, bounds, false) ??
                throw new NotSupportedException("The requested array type is not available in this compiled program.");
        }

        // The lower-bounded form has its own intrinsic, so only a program that passes lower bounds carries the
        // rank-one arrays with lower bounds (T[*]) that only this form creates.
        private static unsafe Array CreateBoundedInstance(Type elementType, int[] lengths, int[] lowerBounds)
        {
            var rank = CheckedRank(lengths);
            var bounds = stackalloc int[rank * 2];
            var bounded = CopyShape(lengths, lowerBounds, bounds);
            if (rank == 1 && !bounded) return CreateInstance(elementType, bounds[0]);
            return InternalCreateBoundedInstance(elementType.SemanticTypeId, rank, bounds, bounded) ??
                CreateSharedInstance(elementType, rank, bounds, bounded) ??
                throw new NotSupportedException("The requested array type is not available in this compiled program.");
        }

        // An array of a reference element the program has no array type of: the shared array of its shape
        // (SharedArrays), or null where the program has none of that shape or the element cannot be shared.
        private static unsafe Array? CreateSharedInstance(Type elementType, int rank, int* bounds, bool bounded) =>
            SharedArrays.Holds(elementType)
                ? InternalCreateSharedInstance(elementType.SemanticTypeId, rank, bounds, bounded)
                : null;

        private static int CheckedRank(int[] lengths)
        {
            const int MaxRank = 32;
            if (lengths.Length > MaxRank)
                throw new NotSupportedException("The requested array type is not available in this compiled program.");
            return lengths.Length;
        }

        // The lengths, then the lower bounds; whether any lower bound is not zero.
        private static unsafe bool CopyShape(int[] lengths, int[]? lowerBounds, int* bounds)
        {
            var rank = lengths.Length;
            var bounded = false;
            for (var dimension = 0; dimension < rank; dimension++)
            {
                var lowerBound = lowerBounds is null ? 0 : lowerBounds[dimension];
                if ((long)lowerBound + lengths[dimension] > (long)int.MaxValue + 1)
                    throw new ArgumentOutOfRangeException();
                bounds[dimension] = lengths[dimension];
                bounds[rank + dimension] = lowerBound;
                bounded |= lowerBound != 0;
            }
            return bounded;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        private static extern Array? InternalCreateInstance(int elementTypeId, int length);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        private static extern unsafe Array? InternalCreateRectangularInstance(
            int elementTypeId, int rank, int* bounds, bool bounded);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        private static extern unsafe Array? InternalCreateBoundedInstance(
            int elementTypeId, int rank, int* bounds, bool bounded);

        // The shared array of the rank and boundedness, with the element in its header (the lengths, then for a
        // bounded shape the lower bounds, at bounds); null where the program has no shared array of that shape.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        private static extern unsafe Array? InternalCreateSharedInstance(
            int elementTypeId, int rank, int* bounds, bool bounded);

        // The array's element type, from its header.
        internal static Type ElementTypeOf(Array array) => InternalElementType(array);

        // Compiler intrinsics: the array's element type, and the runtime's store of a value of the
        // element's own type, which returns nonzero for any other value.
        private static Type InternalElementType(Array array) => throw new PlatformNotSupportedException();
        private static int InternalSetValue(Array array, int offset, object? value) =>
            throw new PlatformNotSupportedException();

        public void CopyTo(Array array, int index)
        {
            Copy(this, 0, array, index, Length);
        }

        public void CopyTo(Array array, long index) => CopyTo(array, ToInt32(index));

        [Intrinsic]
        public object Clone()
        {
            return InternalClone(this);
        }

        private static object InternalClone(Array array) =>
            throw new PlatformNotSupportedException();

        internal static object InternalCloneRange(Array array, int offset, int length) =>
            throw new PlatformNotSupportedException();

        public Collections.IEnumerator GetEnumerator()
        {
            if (this is object[] values) return new SZArrayEnumerator<object>(values);
            return new ArrayEnumerator(this);
        }

        private static object? InternalGetValue(Array array, int offset) =>
            throw new PlatformNotSupportedException();

        private sealed class ArrayEnumerator(Array array) : Collections.IEnumerator, ICloneable
        {
            private int _index = -1;

            public object Clone()
            {
                var clone = new ArrayEnumerator(array);
                clone._index = _index;
                return clone;
            }

            public object Current => _index >= 0 && _index < array.Length
                ? InternalGetValue(array, _index)!
                : throw new InvalidOperationException();

            public bool MoveNext()
            {
                var next = _index + 1;
                if (next < array.Length)
                {
                    _index = next;
                    return true;
                }
                _index = array.Length;
                return false;
            }

            public void Reset() => _index = -1;
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

        // Array's argument errors name their own message and parameter, as upstream's ThrowHelper calls produce them, so a
        // program that reaches Array's checks carries only these strings: ThrowHelper maps its enums through switches that
        // hold every CoreLib message.
        [DoesNotReturn]
        private static void ThrowArgumentNull(string paramName) => throw new ArgumentNullException(paramName);

        [DoesNotReturn]
        private static void ThrowArgumentOutOfRange(string paramName, string message) =>
            throw new ArgumentOutOfRangeException(paramName, message);

        [DoesNotReturn]
        private static void ThrowArgument(string message, string? paramName) => throw new ArgumentException(message, paramName);

        [DoesNotReturn]
        private static void ThrowRank(string message) => throw new RankException(message);

        // An index and a length into a vector, checked as upstream's BinarySearch, Reverse and Sort check them.
        private static void ValidateIndexLength<T>([NotNull] T[]? array, int index, int length)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (index < 0)
            {
                ThrowArgumentOutOfRange("index", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (length < 0)
            {
                ThrowArgumentOutOfRange("length", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (array.Length - index < length)
            {
                ThrowArgument(SR.Argument_InvalidOffLen, null);
            }
        }

        // The same for an Array, whose indices start at its lower bound, which it returns; the rank is checked last,
        // as upstream's BinarySearch(Array, ...) and Reverse(Array, ...) check it.
        private static int ValidateVectorIndexLength([NotNull] Array? array, int index, int length)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            var lowerBound = array.GetLowerBound(0);
            if (index < lowerBound)
            {
                ThrowArgumentOutOfRange("index", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (length < 0)
            {
                ThrowArgumentOutOfRange("length", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (array.Length - (index - lowerBound) < length)
            {
                ThrowArgument(SR.Argument_InvalidOffLen, null);
            }
            if (array.Rank != 1)
            {
                ThrowRank(SR.Rank_MultiDimNotSupported);
            }
            return lowerBound;
        }

        // A copy's two ranges, by physical offset, checked as upstream's CopyImpl (Array.CoreCLR.cs) checks them: the
        // ranks, the length, each index against its array's lower bound, then each range against its array's length.
        private static void ValidateCopy(Array sourceArray, long sourceOffset, Array destinationArray,
            long destinationOffset, int length)
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            if (sourceArray.Rank != destinationArray.Rank)
            {
                ThrowRank(SR.Rank_MustMatch);
            }
            if (length < 0)
            {
                ThrowArgumentOutOfRange("length", SR.ArgumentOutOfRange_NeedNonNegNum);
            }
            if (sourceOffset < 0)
            {
                ThrowArgumentOutOfRange("sourceIndex", SR.ArgumentOutOfRange_ArrayLB);
            }
            if (destinationOffset < 0)
            {
                ThrowArgumentOutOfRange("destinationIndex", SR.ArgumentOutOfRange_ArrayLB);
            }
            if (sourceOffset + length > sourceArray.Length)
            {
                ThrowArgument(SR.Arg_LongerThanSrcArray, "sourceArray");
            }
            if (destinationOffset + length > destinationArray.Length)
            {
                ThrowArgument(SR.Arg_LongerThanDestArray, "destinationArray");
            }
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern ArrayCopyStatus InternalCopy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length);

        private enum ArrayCopyStatus
        {
            Success,
            TypeMismatch,
            ElementCastFailure,
            OutOfMemory,
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void InternalClear(Array array, int index, int length);

        // LastIndexOf<T>'s start and count, checked as upstream checks them.
        private static void ValidateLastSearchRange<T>([NotNull] T[]? array, int startIndex, int count)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (array.Length == 0)
            {
                if (startIndex != -1 && startIndex != 0)
                {
                    ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLess);
                }
                if (count != 0)
                {
                    ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
                }
                return;
            }
            if ((uint)startIndex >= (uint)array.Length)
            {
                ThrowArgumentOutOfRange("startIndex", SR.ArgumentOutOfRange_IndexMustBeLess);
            }
            if (count < 0 || startIndex - count + 1 < 0)
            {
                ThrowArgumentOutOfRange("count", SR.ArgumentOutOfRange_Count);
            }
        }

        private static void ValidatePredicate<T>([NotNull] T[]? array, [NotNull] Predicate<T>? match)
        {
            if (array is null)
            {
                ThrowArgumentNull("array");
            }
            if (match is null)
            {
                ThrowArgumentNull("match");
            }
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
                if (right is IComparable reverseComparable)
                {
                    return -reverseComparable.CompareTo(left);
                }
                throw new ArgumentException("At least one object must implement IComparable.");
            }
        }

        private sealed class NonGenericComparerAdapter(IComparer comparer) : IComparer<object?>
        {
            public int Compare(object? left, object? right) => comparer.Compare(left, right);
        }

        private sealed class ArrayIndexComparer(
            Array keys,
            int sourceOffset,
            IComparer comparer) : IComparer<int>
        {
            public int Compare(int left, int right) => comparer.Compare(
                InternalGetValue(keys, sourceOffset + left),
                InternalGetValue(keys, sourceOffset + right));
        }
    }
}
