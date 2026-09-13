// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Portable scalar adaptation of System.Private.CoreLib Vector_1.cs at
// runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.  This target has
// no SIMD lane type; storage and operations therefore remain managed and
// scalar while preserving the public Vector<T> contract.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Numerics
{
    public struct Vector<T> : IEquatable<Vector<T>>, IFormattable
    {
        private T[]? _elements;

        public Vector(T value)
        {
            _elements = new T[Count];
            for (var index = 0; index < _elements.Length; index++) _elements[index] = value;
        }

        public Vector(T[] values)
        {
            if (values.Length < Count) throw new ArgumentOutOfRangeException(nameof(values));
            _elements = new T[Count];
            for (var index = 0; index < Count; index++) _elements[index] = values[index];
        }

        public Vector(T[] values, int index)
        {
            if (index < 0 || values.Length - index < Count) throw new ArgumentOutOfRangeException(nameof(index));
            _elements = new T[Count];
            for (var offset = 0; offset < Count; offset++) _elements[offset] = values[index + offset];
        }

        public Vector(ReadOnlySpan<T> values)
        {
            if (values.Length < Count) throw new ArgumentOutOfRangeException(nameof(values));
            _elements = new T[Count];
            for (var index = 0; index < Count; index++) _elements[index] = values[index];
        }

        public Vector(Span<T> values) : this((ReadOnlySpan<T>)values) { }

        public Vector(ReadOnlySpan<byte> values)
        {
            if (typeof(T) != typeof(byte)) throw new NotSupportedException();
            if (values.Length < Count) throw new ArgumentOutOfRangeException(nameof(values));
            _elements = new T[Count];
            for (var index = 0; index < Count; index++) _elements[index] = (T)(object)values[index];
        }

        public static Vector<T> AllBitsSet
        {
            [Intrinsic]
            get => new(VectorScalar<T>.AllBitsSet);
        }

        public static int Count
        {
            [Intrinsic]
            get => VectorScalar<T>.VectorElementCount;
        }

        public static Vector<T> Indices
        {
            get
            {
                var result = new Vector<T>();
                result._elements = new T[Count];
                for (var index = 0; index < Count; index++) result._elements[index] = VectorScalar<T>.Convert(index);
                return result;
            }
        }

        public static bool IsSupported
        {
            [Intrinsic]
            get => VectorScalar<T>.IsSupported;
        }

        public static Vector<T> One
        {
            [Intrinsic]
            get => new(VectorScalar<T>.One);
        }

        public static Vector<T> Zero
        {
            [Intrinsic]
            get => new();
        }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _elements is null ? default! : _elements[index];
            }
        }

        public static Vector<T> operator +(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.Add);
        public static Vector<T> operator -(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.Subtract);
        public static Vector<T> operator *(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.Multiply);
        public static Vector<T> operator /(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.Divide);
        public static Vector<T> operator *(Vector<T> value, T factor) => value * new Vector<T>(factor);
        public static Vector<T> operator *(T factor, Vector<T> value) => value * factor;
        public static Vector<T> operator /(Vector<T> value, T factor) => value / new Vector<T>(factor);
        public static Vector<T> operator +(Vector<T> value) => value;
        public static Vector<T> operator -(Vector<T> value) => VectorScalar<T>.Map(value, VectorScalar<T>.Negate);
        public static Vector<T> operator <<(Vector<T> value, int shiftCount) => VectorScalar<T>.Map(value, element => VectorScalar<T>.ShiftLeft(element, shiftCount));
        public static Vector<T> operator >>(Vector<T> value, int shiftCount) => VectorScalar<T>.Map(value, element => VectorScalar<T>.ShiftRightArithmetic(element, shiftCount));
        public static Vector<T> operator >>>(Vector<T> value, int shiftCount) => VectorScalar<T>.Map(value, element => VectorScalar<T>.ShiftRightLogical(element, shiftCount));
        public static Vector<T> operator &(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.BitwiseAnd);
        public static Vector<T> operator |(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.BitwiseOr);
        public static Vector<T> operator ^(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.BitwiseXor);
        public static Vector<T> operator ~(Vector<T> value) => VectorScalar<T>.Map(value, VectorScalar<T>.OnesComplement);
        public static bool operator ==(Vector<T> left, Vector<T> right) => left.Equals(right);
        public static bool operator !=(Vector<T> left, Vector<T> right) => !left.Equals(right);

        public static explicit operator Vector<byte>(Vector<T> value) => Vector<T>.Reinterpret<byte>(value);
        public static explicit operator Vector<sbyte>(Vector<T> value) => Vector<T>.Reinterpret<sbyte>(value);
        public static explicit operator Vector<short>(Vector<T> value) => Vector<T>.Reinterpret<short>(value);
        public static explicit operator Vector<ushort>(Vector<T> value) => Vector<T>.Reinterpret<ushort>(value);
        public static explicit operator Vector<int>(Vector<T> value) => Vector<T>.Reinterpret<int>(value);
        public static explicit operator Vector<uint>(Vector<T> value) => Vector<T>.Reinterpret<uint>(value);
        public static explicit operator Vector<long>(Vector<T> value) => Vector<T>.Reinterpret<long>(value);
        public static explicit operator Vector<ulong>(Vector<T> value) => Vector<T>.Reinterpret<ulong>(value);
        public static explicit operator Vector<nint>(Vector<T> value) => Vector<T>.Reinterpret<nint>(value);
        public static explicit operator Vector<nuint>(Vector<T> value) => Vector<T>.Reinterpret<nuint>(value);
        public static explicit operator Vector<float>(Vector<T> value) => Vector<T>.Reinterpret<float>(value);
        public static explicit operator Vector<double>(Vector<T> value) => Vector<T>.Reinterpret<double>(value);

        public void CopyTo(T[] destination) => CopyTo(destination, 0);

        public void CopyTo(T[] destination, int index)
        {
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            if (index < 0 || destination.Length - index < Count) throw new ArgumentException();
            for (var offset = 0; offset < Count; offset++) destination[index + offset] = this[offset];
        }

        public void CopyTo(Span<T> destination)
        {
            if (destination.Length < Count) throw new ArgumentException();
            for (var index = 0; index < Count; index++) destination[index] = this[index];
        }

        public void CopyTo(Span<byte> destination)
        {
            if (typeof(T) != typeof(byte)) throw new NotSupportedException();
            if (destination.Length < Count) throw new ArgumentException();
            for (var index = 0; index < Count; index++) destination[index] = (byte)(object)this[index]!;
        }

        public bool TryCopyTo(Span<T> destination)
        {
            if (destination.Length < Count) return false;
            CopyTo(destination);
            return true;
        }

        public bool TryCopyTo(Span<byte> destination)
        {
            if (destination.Length < Count) return false;
            CopyTo(destination);
            return true;
        }

        public bool Equals(Vector<T> other)
        {
            for (var index = 0; index < Count; index++)
            {
                if (!VectorScalar<T>.ObjectEquals(this[index], other[index])) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is Vector<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            for (var index = 0; index < Count; index++) hash = (hash * 31) + (this[index]?.GetHashCode() ?? 0);
            return hash;
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string? format) => ToString(format, null);

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            var builder = new Text.StringBuilder();
            builder.Append('<');
            for (var index = 0; index < Count; index++)
            {
                if (index != 0) builder.Append(", ");
                if (this[index] is IFormattable formattable) builder.Append(formattable.ToString(format, formatProvider));
                else builder.Append(this[index]?.ToString());
            }
            builder.Append('>');
            return builder.ToString();
        }

        internal void SetElementUnsafe(int index, T value)
        {
            _elements ??= new T[Count];
            _elements[index] = value;
        }

        internal static Vector<TTo> Reinterpret<TTo>(Vector<T> value)
        {
            var result = new Vector<TTo>();
            result._elements = new TTo[Vector<TTo>.Count];
            var length = result._elements.Length < Count ? result._elements.Length : Count;
            for (var index = 0; index < length; index++) result._elements[index] = VectorScalar<TTo>.ConvertObject(value[index]);
            return result;
        }
    }

    internal static class VectorScalar<T>
    {
        internal static readonly bool IsSupported = typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) ||
            typeof(T) == typeof(short) || typeof(T) == typeof(ushort) || typeof(T) == typeof(int) ||
            typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong) ||
            typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) || typeof(T) == typeof(float) ||
            typeof(T) == typeof(double);

        internal static readonly bool IsUnsigned = typeof(T) == typeof(byte) || typeof(T) == typeof(ushort) ||
            typeof(T) == typeof(uint) || typeof(T) == typeof(ulong) || typeof(T) == typeof(nuint);

        internal static readonly int VectorElementCount = ElementSize == 0 ? 0 : 16 / ElementSize;
        private static int ElementSize => typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) ? 1 :
            typeof(T) == typeof(short) || typeof(T) == typeof(ushort) ? 2 :
            typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(float) ? 4 :
            typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) ? IntPtr.Size :
            typeof(T) == typeof(long) || typeof(T) == typeof(ulong) || typeof(T) == typeof(double) ? 8 : 0;

        internal static T AllBitsSet => FromInt64(-1);
        internal static T Zero => default!;
        internal static T One => FromInt64(1);
        internal static T Convert(int value) => FromInt64(value);
        internal static TTo ConvertValue<TTo>(T value)
        {
            var number = ToDouble(value);
            object result = typeof(TTo) == typeof(byte) ? (byte)number : typeof(TTo) == typeof(sbyte) ? (sbyte)number :
                typeof(TTo) == typeof(short) ? (short)number : typeof(TTo) == typeof(ushort) ? (ushort)number :
                typeof(TTo) == typeof(int) ? (int)number : typeof(TTo) == typeof(uint) ? (uint)number :
                typeof(TTo) == typeof(long) ? (long)number : typeof(TTo) == typeof(ulong) ? (ulong)number :
                typeof(TTo) == typeof(float) ? (float)number : typeof(TTo) == typeof(double) ? number :
                throw new NotSupportedException();
            return (TTo)result;
        }

        internal static T Add(T left, T right) => Binary(left, right, 0);
        internal static T Subtract(T left, T right) => Binary(left, right, 1);
        internal static T Multiply(T left, T right) => Binary(left, right, 2);
        internal static T Divide(T left, T right) => Binary(left, right, 3);
        internal static T AddSaturate(T left, T right) => Binary(left, right, 0);
        internal static T Negate(T value) => Unary(value, 0);
        internal static T Abs(T value) => Unary(value, 1);
        internal static T OnesComplement(T value) => Unary(value, 2);
        internal static T BitwiseAnd(T left, T right) => Binary(left, right, 4);
        internal static T BitwiseOr(T left, T right) => Binary(left, right, 5);
        internal static T BitwiseXor(T left, T right) => Binary(left, right, 6);
        internal static T ShiftLeft(T value, int shift) => Shift(value, shift, 0);
        internal static T ShiftRightArithmetic(T value, int shift) => Shift(value, shift, 1);
        internal static T ShiftRightLogical(T value, int shift) => Shift(value, shift, 2);
        internal static bool ObjectEquals(T left, T right) => Equals(left, right);
        internal static bool Equals(T left, T right) => object.Equals(left, right);
        internal static bool GreaterThan(T left, T right) => Compare(left, right) > 0;
        internal static bool GreaterThanOrEqual(T left, T right) => Compare(left, right) >= 0;
        internal static bool LessThan(T left, T right) => Compare(left, right) < 0;
        internal static bool LessThanOrEqual(T left, T right) => Compare(left, right) <= 0;
        internal static T Max(T left, T right) => GreaterThan(left, right) ? left : right;
        internal static T Min(T left, T right) => LessThan(left, right) ? left : right;
        internal static T CopySign(T value, T sign) => IsNegativeValue(sign) ? Negate(value) : Abs(value);
        internal static T SquareRoot(T value) => typeof(T) == typeof(float) ? (T)(object)(float)Math.Sqrt((float)(object)value!) : typeof(T) == typeof(double) ? (T)(object)Math.Sqrt((double)(object)value!) : throw new NotSupportedException();
        internal static T IsEvenInteger(T value) => IsIntegerValue(value) && (ToDouble(value) % 2) == 0 ? AllBitsSet : Zero;
        internal static T IsOddInteger(T value) => IsIntegerValue(value) && (ToDouble(value) % 2) != 0 ? AllBitsSet : Zero;
        internal static T IsInteger(T value) => ToDouble(value) == Math.Truncate(ToDouble(value)) ? AllBitsSet : Zero;
        internal static T IsFinite(T value) => typeof(T) == typeof(float) ? (T)(object)(float.IsFinite((float)(object)value!) ? -1 : 0) : typeof(T) == typeof(double) ? (T)(object)(double.IsFinite((double)(object)value!) ? -1L : 0L) : AllBitsSet;
        internal static T IsInfinity(T value) => typeof(T) == typeof(float) ? (T)(object)(float.IsInfinity((float)(object)value!) ? -1 : 0) : typeof(T) == typeof(double) ? (T)(object)(double.IsInfinity((double)(object)value!) ? -1L : 0L) : Zero;
        internal static T IsNaN(T value) => typeof(T) == typeof(float) ? (T)(object)(float.IsNaN((float)(object)value!) ? -1 : 0) : typeof(T) == typeof(double) ? (T)(object)(double.IsNaN((double)(object)value!) ? -1L : 0L) : Zero;
        internal static T IsNegative(T value) => ToDouble(value) < 0 ? AllBitsSet : Zero;
        internal static T IsNegativeInfinity(T value) => typeof(T) == typeof(float) ? (T)(object)(float.IsNegativeInfinity((float)(object)value!) ? -1 : 0) : typeof(T) == typeof(double) ? (T)(object)(double.IsNegativeInfinity((double)(object)value!) ? -1L : 0L) : Zero;
        internal static T IsNormal(T value) => IsFinite(value);
        internal static T IsPositive(T value) => ToDouble(value) > 0 ? AllBitsSet : Zero;
        internal static T IsPositiveInfinity(T value) => typeof(T) == typeof(float) ? (T)(object)(float.IsPositiveInfinity((float)(object)value!) ? -1 : 0) : typeof(T) == typeof(double) ? (T)(object)(double.IsPositiveInfinity((double)(object)value!) ? -1L : 0L) : Zero;
        internal static T IsSubnormal(T value) => Zero;

        private static bool IsNegativeValue(T value) => ToDouble(value) < 0;
        private static bool IsIntegerValue(T value) => ToDouble(value) == Math.Truncate(ToDouble(value));

        internal static Vector<T> Map(Vector<T> value, Func<T, T> operation)
        {
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, operation(value[index]));
            return result;
        }

        internal static Vector<T> Map(Vector<T> left, Vector<T> right, Func<T, T, T> operation)
        {
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, operation(left[index], right[index]));
            return result;
        }

        internal static T ConvertObject<TValue>(TValue value) => (T)(object)value!;

        private static T Binary(T left, T right, int operation)
        {
            if (typeof(T) == typeof(float)) return (T)(object)FloatBinary((float)(object)left!, (float)(object)right!, operation);
            if (typeof(T) == typeof(double)) return (T)(object)DoubleBinary((double)(object)left!, (double)(object)right!, operation);
            var l = ToUInt64(left);
            var r = ToUInt64(right);
            var bits = ElementSize * 8;
            var result = operation switch
            {
                0 => l + r,
                1 => l - r,
                2 => l * r,
                3 => l / r,
                4 => l & r,
                5 => l | r,
                _ => l ^ r,
            };
            return FromUInt64(Mask(result, bits));
        }

        private static int Compare(T left, T right)
        {
            if (typeof(T) == typeof(float)) return ((float)(object)left!).CompareTo((float)(object)right!);
            if (typeof(T) == typeof(double)) return ((double)(object)left!).CompareTo((double)(object)right!);
            var l = ToDouble(left);
            var r = ToDouble(right);
            return l < r ? -1 : l > r ? 1 : 0;
        }

        private static double ToDouble(T value) => value switch
        {
            byte v => v,
            sbyte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            nint v => v,
            nuint v => v,
            float v => v,
            double v => v,
            _ => throw new NotSupportedException(),
        };

        private static T Unary(T value, int operation)
        {
            if (typeof(T) == typeof(float))
            {
                var v = (float)(object)value!;
                return (T)(object)(operation == 0 ? -v : operation == 1 ? Math.Abs(v) : BitwiseFloat(v));
            }
            if (typeof(T) == typeof(double))
            {
                var v = (double)(object)value!;
                return (T)(object)(operation == 0 ? -v : operation == 1 ? Math.Abs(v) : BitwiseDouble(v));
            }
            var result = ToUInt64(value);
            if (operation == 0) result = 0 - result;
            else if (operation == 1) result = IsUnsigned ? result : (ulong)Math.Abs((long)result);
            else result = ~result;
            return FromUInt64(Mask(result, ElementSize * 8));
        }

        private static T Shift(T value, int shift, int operation)
        {
            var bits = ElementSize * 8;
            shift &= bits - 1;
            var unsigned = ToUInt64(value);
            var result = operation == 0 ? unsigned << shift : operation == 2 ? unsigned >> shift : SignedToUInt64(value) >> shift;
            return FromUInt64(Mask(result, bits));
        }

        private static float FloatBinary(float left, float right, int operation) => operation switch { 0 => left + right, 1 => left - right, 2 => left * right, 3 => left / right, _ => throw new NotSupportedException() };
        private static double DoubleBinary(double left, double right, int operation) => operation switch { 0 => left + right, 1 => left - right, 2 => left * right, 3 => left / right, _ => throw new NotSupportedException() };
        private static float BitwiseFloat(float value) => value;
        private static double BitwiseDouble(double value) => value;
        private static ulong Mask(ulong value, int bits) => bits == 64 ? value : value & ((1ul << bits) - 1);

        private static ulong ToUInt64(T value) => value switch
        {
            byte v => v,
            sbyte v => unchecked((ulong)v),
            short v => unchecked((ulong)v),
            ushort v => v,
            int v => unchecked((ulong)v),
            uint v => v,
            long v => unchecked((ulong)v),
            ulong v => v,
            nint v => unchecked((ulong)v),
            nuint v => v,
            _ => throw new NotSupportedException(),
        };

        private static ulong SignedToUInt64(T value) => unchecked((ulong)(long)ToUInt64(value));
        private static T FromInt64(long value) => FromUInt64(unchecked((ulong)value));
        private static T FromUInt64(ulong value) => typeof(T) == typeof(byte) ? (T)(object)(byte)value :
            typeof(T) == typeof(sbyte) ? (T)(object)(sbyte)value : typeof(T) == typeof(short) ? (T)(object)(short)value :
            typeof(T) == typeof(ushort) ? (T)(object)(ushort)value : typeof(T) == typeof(int) ? (T)(object)(int)value :
            typeof(T) == typeof(uint) ? (T)(object)(uint)value : typeof(T) == typeof(long) ? (T)(object)(long)value :
            typeof(T) == typeof(ulong) ? (T)(object)value : typeof(T) == typeof(nint) ? (T)(object)(nint)(long)value :
            typeof(T) == typeof(nuint) ? (T)(object)(nuint)value : throw new NotSupportedException();
    }
}
