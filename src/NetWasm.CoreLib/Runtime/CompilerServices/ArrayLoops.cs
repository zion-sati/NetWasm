using System.Numerics;
using System.Runtime.Intrinsics;

namespace System.Runtime.CompilerServices
{
    // The compiler calls these ahead of a counted loop that only stores one
    // value into consecutive array elements, only copies elements between two
    // arrays at the same index, or only folds elements into locals. It emits
    // Fill, Copy and the bitwise folds inline; the sums, minimums and maximums
    // are vector code of their own. Fill and Copy do the loop's iterations from start that
    // stay within the arrays, so none can throw, and return the index the loop
    // goes on from: end, or where the array ends, or start when a null array or
    // an invalid start leaves the first iteration to throw.
    internal static class ArrayLoops
    {
        internal static int Fill<T>(T[]? array, int start, int end, T value) =>
            throw new InvalidOperationException("The NetWasm compiler did not lower the array fill.");

        internal static int Copy<T>(T[]? destination, T[]? source, int start, int end) =>
            throw new InvalidOperationException("The NetWasm compiler did not lower the array copy.");

        // The bitwise exclusive or, or and and of the count integer elements
        // from data on, each widened as its type's sign says: 32-bit for
        // elements of up to four bytes, 64-bit for eight. The loop checked that
        // every element lies within its array or span.
        internal static int Xor32<T>(ref T data, int count) => throw Unlowered();

        internal static long Xor64<T>(ref T data, int count) => throw Unlowered();

        internal static int Or32<T>(ref T data, int count) => throw Unlowered();

        internal static long Or64<T>(ref T data, int count) => throw Unlowered();

        internal static int And32<T>(ref T data, int count) => throw Unlowered();

        internal static long And64<T>(ref T data, int count) => throw Unlowered();

        // The wrapping sum of the count elements from data on, each widened as
        // its type's sign says: to 32 bits for elements of up to four bytes, and
        // to 64 for Sum64. Count is at least one, and the loop checked that every
        // element lies within its array or span.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Sum32<T>(ref T data, int count)
        {
            if (typeof(T) == typeof(sbyte)) return Sum(ref Unsafe.As<T, sbyte>(ref data), count);
            if (typeof(T) == typeof(byte)) return Sum(ref Unsafe.As<T, byte>(ref data), count);
            if (typeof(T) == typeof(short)) return Sum(ref Unsafe.As<T, short>(ref data), count);
            if (typeof(T) == typeof(ushort) || typeof(T) == typeof(char)) return Sum(ref Unsafe.As<T, ushort>(ref data), count);
            return Sum(ref Unsafe.As<T, int>(ref data), count);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static long Sum64<T>(ref T data, int count)
        {
            if (typeof(T) == typeof(int)) return WidenedSum(ref Unsafe.As<T, int>(ref data), count);
            if (typeof(T) == typeof(uint)) return (long)WidenedSum(ref Unsafe.As<T, uint>(ref data), count);
            if (typeof(T) == typeof(long) || typeof(T) == typeof(ulong)) return Sum(ref Unsafe.As<T, long>(ref data), count);
            // 2^15 elements of up to two bytes sum exactly in 32 bits.
            var sum = 0L;
            for (var index = 0; index < count;)
            {
                var part = Math.Min(count - index, 1 << 15);
                sum += Sum32(ref Unsafe.Add(ref data, index), part);
                index += part;
            }
            return sum;
        }

        // The least or greatest of the count elements from data on, widened as
        // its type's sign says: to 32 bits for elements of up to four bytes, and
        // to 64 for Min64 and Max64.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Min32<T>(ref T data, int count) => Extreme32(ref data, count, maximum: false);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Max32<T>(ref T data, int count) => Extreme32(ref data, count, maximum: true);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static long Min64<T>(ref T data, int count) => Extreme64(ref data, count, maximum: false);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static long Max64<T>(ref T data, int count) => Extreme64(ref data, count, maximum: true);

        private static int Sum(ref sbyte data, int count)
        {
            var total = Vector128<int>.Zero;
            var index = 0;
            for (; index <= count - Vector128<sbyte>.Count; index += Vector128<sbyte>.Count)
            {
                var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                // Two widened bytes fit a 16-bit lane.
                var pairs = Vector128.WidenLower(value) + Vector128.WidenUpper(value);
                total += Vector128.WidenLower(pairs) + Vector128.WidenUpper(pairs);
            }
            var sum = Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static int Sum(ref byte data, int count)
        {
            var total = Vector128<uint>.Zero;
            var index = 0;
            for (; index <= count - Vector128<byte>.Count; index += Vector128<byte>.Count)
            {
                var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                var pairs = Vector128.WidenLower(value) + Vector128.WidenUpper(value);
                total += Vector128.WidenLower(pairs) + Vector128.WidenUpper(pairs);
            }
            var sum = (int)Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static int Sum(ref short data, int count)
        {
            var total = Vector128<int>.Zero;
            var index = 0;
            for (; index <= count - Vector128<short>.Count; index += Vector128<short>.Count)
            {
                var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                total += Vector128.WidenLower(value) + Vector128.WidenUpper(value);
            }
            var sum = Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static int Sum(ref ushort data, int count)
        {
            var total = Vector128<uint>.Zero;
            var index = 0;
            for (; index <= count - Vector128<ushort>.Count; index += Vector128<ushort>.Count)
            {
                var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                total += Vector128.WidenLower(value) + Vector128.WidenUpper(value);
            }
            var sum = (int)Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static int Sum(ref int data, int count)
        {
            var total = Vector128<int>.Zero;
            var index = 0;
            for (; index <= count - Vector128<int>.Count; index += Vector128<int>.Count)
            {
                total += Vector128.LoadUnsafe(ref data, (nuint)index);
            }
            var sum = Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static long WidenedSum(ref int data, int count)
        {
            var total = Vector128<long>.Zero;
            var index = 0;
            for (; index <= count - Vector128<int>.Count; index += Vector128<int>.Count)
            {
                var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                total += Vector128.WidenLower(value) + Vector128.WidenUpper(value);
            }
            var sum = Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static ulong WidenedSum(ref uint data, int count)
        {
            var total = Vector128<ulong>.Zero;
            var index = 0;
            for (; index <= count - Vector128<uint>.Count; index += Vector128<uint>.Count)
            {
                var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                total += Vector128.WidenLower(value) + Vector128.WidenUpper(value);
            }
            var sum = Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static long Sum(ref long data, int count)
        {
            var total = Vector128<long>.Zero;
            var index = 0;
            for (; index <= count - Vector128<long>.Count; index += Vector128<long>.Count)
            {
                total += Vector128.LoadUnsafe(ref data, (nuint)index);
            }
            var sum = Vector128.Sum(total);
            for (; index < count; index++) sum += Unsafe.Add(ref data, index);
            return sum;
        }

        private static int Extreme32<T>(ref T data, int count, bool maximum)
        {
            if (typeof(T) == typeof(sbyte)) return Extreme(ref Unsafe.As<T, sbyte>(ref data), count, maximum);
            if (typeof(T) == typeof(byte)) return Extreme(ref Unsafe.As<T, byte>(ref data), count, maximum);
            if (typeof(T) == typeof(short)) return Extreme(ref Unsafe.As<T, short>(ref data), count, maximum);
            if (typeof(T) == typeof(ushort) || typeof(T) == typeof(char)) return Extreme(ref Unsafe.As<T, ushort>(ref data), count, maximum);
            if (typeof(T) == typeof(uint)) return (int)Extreme(ref Unsafe.As<T, uint>(ref data), count, maximum);
            return Extreme(ref Unsafe.As<T, int>(ref data), count, maximum);
        }

        private static long Extreme64<T>(ref T data, int count, bool maximum) =>
            typeof(T) == typeof(ulong)
                ? (long)Extreme(ref Unsafe.As<T, ulong>(ref data), count, maximum)
                : Extreme(ref Unsafe.As<T, long>(ref data), count, maximum);

        private static TLane Extreme<TLane>(ref TLane data, int count, bool maximum)
            where TLane : unmanaged, IBinaryInteger<TLane>
        {
            var result = data;
            var index = 1;
            if (count >= Vector128<TLane>.Count)
            {
                var extreme = Vector128.LoadUnsafe(ref data);
                for (index = Vector128<TLane>.Count; index <= count - Vector128<TLane>.Count; index += Vector128<TLane>.Count)
                {
                    var value = Vector128.LoadUnsafe(ref data, (nuint)index);
                    extreme = maximum ? Vector128.Max(extreme, value) : Vector128.Min(extreme, value);
                }
                result = extreme.ToScalar();
                for (var lane = 1; lane < Vector128<TLane>.Count; lane++)
                {
                    var value = extreme.GetElement(lane);
                    result = maximum ? TLane.Max(result, value) : TLane.Min(result, value);
                }
            }
            for (; index < count; index++)
            {
                var value = Unsafe.Add(ref data, index);
                result = maximum ? TLane.Max(result, value) : TLane.Min(result, value);
            }
            return result;
        }

        private static InvalidOperationException Unlowered() =>
            new("The NetWasm compiler did not lower the array reduction.");
    }
}
