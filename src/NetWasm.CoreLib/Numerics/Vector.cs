// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Portable scalar adaptation of System.Private.CoreLib Vector.cs at runtime
// commit 811225a482702af7ecc35d817966bc70b88a3a23.  SIMD-specific lowering is
// intentionally absent: this API always reports scalar execution.

using System.Runtime.CompilerServices;

namespace System.Numerics
{
    public static unsafe partial class Vector
    {
        public static bool IsHardwareAccelerated
        {
            [Intrinsic]
            get => false;
        }

        public static Vector<T> Abs<T>(Vector<T> value) => Map(value, VectorScalar<T>.Abs);
        public static Vector<T> Add<T>(Vector<T> left, Vector<T> right) => left + right;
        public static Vector<T> AddSaturate<T>(Vector<T> left, Vector<T> right) => VectorScalar<T>.Map(left, right, VectorScalar<T>.AddSaturate);
        public static bool All<T>(Vector<T> vector, T value) => EqualsAll(vector, Create(value));
        public static bool AllWhereAllBitsSet<T>(Vector<T> vector) => All(vector, VectorScalar<T>.AllBitsSet);
        public static Vector<T> AndNot<T>(Vector<T> left, Vector<T> right) => left & ~right;
        public static bool Any<T>(Vector<T> vector, T value) => EqualsAny(vector, Create(value));
        public static bool AnyWhereAllBitsSet<T>(Vector<T> vector) => Any(vector, VectorScalar<T>.AllBitsSet);
        public static Vector<TTo> As<TFrom, TTo>(this Vector<TFrom> vector) => Vector<TFrom>.Reinterpret<TTo>(vector);
        public static Vector<byte> AsVectorByte<T>(Vector<T> value) => As<T, byte>(value);
        public static Vector<sbyte> AsVectorSByte<T>(Vector<T> value) => As<T, sbyte>(value);
        public static Vector<short> AsVectorInt16<T>(Vector<T> value) => As<T, short>(value);
        public static Vector<ushort> AsVectorUInt16<T>(Vector<T> value) => As<T, ushort>(value);
        public static Vector<int> AsVectorInt32<T>(Vector<T> value) => As<T, int>(value);
        public static Vector<uint> AsVectorUInt32<T>(Vector<T> value) => As<T, uint>(value);
        public static Vector<long> AsVectorInt64<T>(Vector<T> value) => As<T, long>(value);
        public static Vector<ulong> AsVectorUInt64<T>(Vector<T> value) => As<T, ulong>(value);
        public static Vector<nint> AsVectorNInt<T>(Vector<T> value) => As<T, nint>(value);
        public static Vector<nuint> AsVectorNUInt<T>(Vector<T> value) => As<T, nuint>(value);
        public static Vector<float> AsVectorSingle<T>(Vector<T> value) => As<T, float>(value);
        public static Vector<double> AsVectorDouble<T>(Vector<T> value) => As<T, double>(value);
        public static Vector<T> BitwiseAnd<T>(Vector<T> left, Vector<T> right) => left & right;
        public static Vector<T> BitwiseOr<T>(Vector<T> left, Vector<T> right) => left | right;
        public static Vector<T> Clamp<T>(Vector<T> value, Vector<T> min, Vector<T> max)
        {
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, VectorScalar<T>.Min(VectorScalar<T>.Max(value[index], min[index]), max[index]));
            return result;
        }
        public static Vector<T> ClampNative<T>(Vector<T> value, Vector<T> min, Vector<T> max) => Clamp(value, min, max);
        public static Vector<T> ConditionalSelect<T>(Vector<T> condition, Vector<T> left, Vector<T> right) => Map(condition, left, right, (mask, a, b) => VectorScalar<T>.Equals(mask, VectorScalar<T>.Zero) ? b : a);
        public static Vector<T> CopySign<T>(Vector<T> value, Vector<T> sign) => Map(value, sign, VectorScalar<T>.CopySign);
        public static int Count<T>(Vector<T> vector, T value) => CountWhere(vector, value);
        public static int CountWhereAllBitsSet<T>(Vector<T> vector) => Count(vector, VectorScalar<T>.AllBitsSet);
        public static Vector<T> Create<T>(T value) => new(value);
        public static Vector<T> Create<T>(ReadOnlySpan<T> values) => new(values);
        public static Vector<T> CreateScalar<T>(T value) => new(value);
        public static Vector<T> CreateScalarUnsafe<T>(T value) => new(value);
        public static Vector<T> CreateSequence<T>(T start, T step) => CreateGeometricSequence(start, step);
        public static Vector<T> CreateAlternatingSequence<T>(T even, T odd) => CreateSequence(even, VectorScalar<T>.Subtract(odd, even));
        public static Vector<T> CreateGeometricSequence<T>(T initial, T multiplier)
        {
            var result = new Vector<T>();
            var current = initial;
            for (var index = 0; index < Vector<T>.Count; index++)
            {
                result.SetElementUnsafe(index, current);
                current = VectorScalar<T>.Multiply(current, multiplier);
            }
            return result;
        }
        public static Vector<T> CreateHarmonicSequence<T>(T initial, T step) => CreateSequence(initial, step);
        public static Vector<T> Divide<T>(Vector<T> left, Vector<T> right) => left / right;
        public static Vector<T> Divide<T>(Vector<T> left, T right) => left / right;
        public static T Dot<T>(Vector<T> left, Vector<T> right)
        {
            var result = default(T)!;
            for (var index = 0; index < Vector<T>.Count; index++) result = VectorScalar<T>.Add(result, VectorScalar<T>.Multiply(left[index], right[index]));
            return result;
        }
        public static Vector<T> Equals<T>(Vector<T> left, Vector<T> right) => Map(left, right, (a, b) => VectorScalar<T>.Equals(a, b) ? VectorScalar<T>.AllBitsSet : default!);
        public static bool EqualsAll<T>(Vector<T> left, Vector<T> right) => All(left, right, VectorScalar<T>.Equals);
        public static bool EqualsAny<T>(Vector<T> left, Vector<T> right) => Any(left, right, VectorScalar<T>.Equals);
        public static Vector<T> GreaterThan<T>(Vector<T> left, Vector<T> right) => Compare(left, right, VectorScalar<T>.GreaterThan);
        public static bool GreaterThanAll<T>(Vector<T> left, Vector<T> right) => All(left, right, VectorScalar<T>.GreaterThan);
        public static bool GreaterThanAny<T>(Vector<T> left, Vector<T> right) => Any(left, right, VectorScalar<T>.GreaterThan);
        public static Vector<T> GreaterThanOrEqual<T>(Vector<T> left, Vector<T> right) => Compare(left, right, VectorScalar<T>.GreaterThanOrEqual);
        public static bool GreaterThanOrEqualAll<T>(Vector<T> left, Vector<T> right) => All(left, right, VectorScalar<T>.GreaterThanOrEqual);
        public static bool GreaterThanOrEqualAny<T>(Vector<T> left, Vector<T> right) => Any(left, right, VectorScalar<T>.GreaterThanOrEqual);
        public static Vector<T> LessThan<T>(Vector<T> left, Vector<T> right) => Compare(left, right, VectorScalar<T>.LessThan);
        public static bool LessThanAll<T>(Vector<T> left, Vector<T> right) => All(left, right, VectorScalar<T>.LessThan);
        public static bool LessThanAny<T>(Vector<T> left, Vector<T> right) => Any(left, right, VectorScalar<T>.LessThan);
        public static Vector<T> LessThanOrEqual<T>(Vector<T> left, Vector<T> right) => Compare(left, right, VectorScalar<T>.LessThanOrEqual);
        public static bool LessThanOrEqualAll<T>(Vector<T> left, Vector<T> right) => All(left, right, VectorScalar<T>.LessThanOrEqual);
        public static bool LessThanOrEqualAny<T>(Vector<T> left, Vector<T> right) => Any(left, right, VectorScalar<T>.LessThanOrEqual);
        public static int IndexOf<T>(Vector<T> vector, T value) => Find(vector, value, false);
        public static int LastIndexOf<T>(Vector<T> vector, T value) => Find(vector, value, true);
        public static int IndexOfWhereAllBitsSet<T>(Vector<T> vector) => IndexOf(vector, VectorScalar<T>.AllBitsSet);
        public static int LastIndexOfWhereAllBitsSet<T>(Vector<T> vector) => LastIndexOf(vector, VectorScalar<T>.AllBitsSet);
        public static Vector<T> IsEvenInteger<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsEvenInteger);
        public static Vector<T> IsFinite<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsFinite);
        public static Vector<T> IsInfinity<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsInfinity);
        public static Vector<T> IsInteger<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsInteger);
        public static Vector<T> IsNaN<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsNaN);
        public static Vector<T> IsNegative<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsNegative);
        public static Vector<T> IsNegativeInfinity<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsNegativeInfinity);
        public static Vector<T> IsNormal<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsNormal);
        public static Vector<T> IsOddInteger<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsOddInteger);
        public static Vector<T> IsPositive<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsPositive);
        public static Vector<T> IsPositiveInfinity<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsPositiveInfinity);
        public static Vector<T> IsSubnormal<T>(Vector<T> value) => Map(value, VectorScalar<T>.IsSubnormal);
        public static Vector<T> IsZero<T>(Vector<T> value) => Map(value, element => VectorScalar<T>.Equals(element, default!) ? VectorScalar<T>.AllBitsSet : VectorScalar<T>.Zero);
        public static Vector<T> Max<T>(Vector<T> left, Vector<T> right) => Map(left, right, VectorScalar<T>.Max);
        public static Vector<T> MaxNative<T>(Vector<T> left, Vector<T> right) => Max(left, right);
        public static Vector<T> MaxNumber<T>(Vector<T> left, Vector<T> right) => Max(left, right);
        public static Vector<T> MaxMagnitude<T>(Vector<T> left, Vector<T> right) => Max(left, right);
        public static Vector<T> MaxMagnitudeNumber<T>(Vector<T> left, Vector<T> right) => Max(left, right);
        public static Vector<T> Min<T>(Vector<T> left, Vector<T> right) => Map(left, right, VectorScalar<T>.Min);
        public static Vector<T> MinNative<T>(Vector<T> left, Vector<T> right) => Min(left, right);
        public static Vector<T> MinNumber<T>(Vector<T> left, Vector<T> right) => Min(left, right);
        public static Vector<T> MinMagnitude<T>(Vector<T> left, Vector<T> right) => Min(left, right);
        public static Vector<T> MinMagnitudeNumber<T>(Vector<T> left, Vector<T> right) => Min(left, right);
        public static Vector<T> Multiply<T>(Vector<T> left, Vector<T> right) => left * right;
        public static Vector<T> Multiply<T>(Vector<T> left, T right) => left * right;
        public static Vector<T> Multiply<T>(T left, Vector<T> right) => left * right;
        public static Vector<T> Negate<T>(Vector<T> value) => -value;
        public static bool None<T>(Vector<T> vector, T value) => !Any(vector, value);
        public static bool NoneWhereAllBitsSet<T>(Vector<T> vector) => !AnyWhereAllBitsSet(vector);
        public static Vector<T> OnesComplement<T>(Vector<T> value) => ~value;
        public static Vector<T> Reverse<T>(Vector<T> value)
        {
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, value[Vector<T>.Count - index - 1]);
            return result;
        }
        public static Vector<T> ShiftLeft<T>(Vector<T> value, int shiftCount) => value << shiftCount;
        public static Vector<T> ShiftRightArithmetic<T>(Vector<T> value, int shiftCount) => value >> shiftCount;
        public static Vector<T> ShiftRightLogical<T>(Vector<T> value, int shiftCount) => value >>> shiftCount;
        public static Vector<T> SquareRoot<T>(Vector<T> value) => Map(value, VectorScalar<T>.SquareRoot);
        public static T Sum<T>(Vector<T> value) => Dot(value, Vector<T>.One);
        public static T GetElement<T>(this Vector<T> value, int index) => value[index];
        public static T ToScalar<T>(this Vector<T> value) => value[0];
        public static Vector<T> Subtract<T>(Vector<T> left, Vector<T> right) => left - right;
        public static Vector<T> SubtractSaturate<T>(Vector<T> left, Vector<T> right) => left - right;
        public static Vector<T> WithElement<T>(this Vector<T> vector, int index, T value)
        {
            if ((uint)index >= (uint)Vector<T>.Count) throw new ArgumentOutOfRangeException(nameof(index));
            vector.SetElementUnsafe(index, value);
            return vector;
        }
        public static Vector<T> Xor<T>(Vector<T> left, Vector<T> right) => left ^ right;

        public static Plane AsPlane(this Vector4 value) => new(value);
        public static Quaternion AsQuaternion(this Vector4 value) => new(value.X, value.Y, value.Z, value.W);
        public static Vector2 AsVector2(this Vector3 value) => new(value.X, value.Y);
        public static Vector2 AsVector2(this Vector4 value) => new(value.X, value.Y);
        public static Vector3 AsVector3(this Vector2 value) => new(value.X, value.Y, 0f);
        public static Vector3 AsVector3(this Vector4 value) => new(value.X, value.Y, value.Z);
        public static Vector3 AsVector3Unsafe(this Vector2 value) => AsVector3(value);
        public static Vector4 AsVector4(this Plane value) => new(value.Normal, value.D);
        public static Vector4 AsVector4(this Quaternion value) => new(value.X, value.Y, value.Z, value.W);
        public static Vector4 AsVector4(this Vector2 value) => new(value.X, value.Y, 0f, 0f);
        public static Vector4 AsVector4(this Vector3 value) => new(value.X, value.Y, value.Z, 0f);
        public static Vector4 AsVector4Unsafe(this Vector2 value) => AsVector4(value);
        public static Vector4 AsVector4Unsafe(this Vector3 value) => AsVector4(value);
        public static float GetElement(this Vector2 value, int index) => value[index];
        public static float GetElement(this Vector3 value, int index) => value[index];
        public static float GetElement(this Vector4 value, int index) => value[index];
        public static Vector2 WithElement(this Vector2 value, int index, float element) { value[index] = element; return value; }
        public static Vector3 WithElement(this Vector3 value, int index, float element) { value[index] = element; return value; }
        public static Vector4 WithElement(this Vector4 value, int index, float element) { value[index] = element; return value; }
        public static float ToScalar(this Vector2 value) => value.X;
        public static float ToScalar(this Vector3 value) => value.X;
        public static float ToScalar(this Vector4 value) => value.X;
        public static uint ExtractMostSignificantBits(this Vector2 value) => (uint)((value.X < 0 ? 1 : 0) | (value.Y < 0 ? 2 : 0));
        public static uint ExtractMostSignificantBits(this Vector3 value) => (uint)((value.X < 0 ? 1 : 0) | (value.Y < 0 ? 2 : 0) | (value.Z < 0 ? 4 : 0));
        public static uint ExtractMostSignificantBits(this Vector4 value) => (uint)((value.X < 0 ? 1 : 0) | (value.Y < 0 ? 2 : 0) | (value.Z < 0 ? 4 : 0) | (value.W < 0 ? 8 : 0));
        public static Vector<float> ConditionalSelect(Vector<int> condition, Vector<float> left, Vector<float> right) => Select(condition, left, right);
        public static Vector<double> ConditionalSelect(Vector<long> condition, Vector<double> left, Vector<double> right) => Select(condition, left, right);

        public static Vector<double> Ceiling(Vector<double> value) => Map(value, double.Ceiling);
        public static Vector<float> Ceiling(Vector<float> value) => Map(value, static value => (float)double.Ceiling(value));
        public static Vector<double> Floor(Vector<double> value) => Map(value, double.Floor);
        public static Vector<float> Floor(Vector<float> value) => Map(value, static value => (float)double.Floor(value));
        public static Vector<double> Truncate(Vector<double> value) => Map(value, double.Truncate);
        public static Vector<float> Truncate(Vector<float> value) => Map(value, static value => (float)double.Truncate(value));
        public static Vector<double> Round(Vector<double> value) => Map(value, double.Round);
        public static Vector<double> Round(Vector<double> value, MidpointRounding mode) => Map(value, value => double.Round(value, mode));
        public static Vector<float> Round(Vector<float> value) => Map(value, static value => (float)double.Round(value));
        public static Vector<float> Round(Vector<float> value, MidpointRounding mode) => Map(value, value => (float)double.Round(value, mode));
        public static Vector<double> Sin(Vector<double> value) => Map(value, double.Sin);
        public static Vector<float> Sin(Vector<float> value) => Map(value, static value => (float)double.Sin(value));
        public static Vector<double> Cos(Vector<double> value) => Map(value, double.Cos);
        public static Vector<float> Cos(Vector<float> value) => Map(value, static value => (float)double.Cos(value));
        public static Vector<double> Exp(Vector<double> value) => Map(value, double.Exp);
        public static Vector<float> Exp(Vector<float> value) => Map(value, static value => (float)double.Exp(value));
        public static Vector<double> Log(Vector<double> value) => Map(value, double.Log);
        public static Vector<float> Log(Vector<float> value) => Map(value, static value => (float)double.Log(value));
        public static Vector<double> Log2(Vector<double> value) => Map(value, double.Log2);
        public static Vector<float> Log2(Vector<float> value) => Map(value, static value => (float)double.Log2(value));
        public static Vector<double> DegreesToRadians(Vector<double> value) => Map(value, static value => value * (double.Pi / 180));
        public static Vector<float> DegreesToRadians(Vector<float> value) => Map(value, static value => (float)(value * (double.Pi / 180)));
        public static Vector<double> RadiansToDegrees(Vector<double> value) => Map(value, static value => value * (180 / double.Pi));
        public static Vector<float> RadiansToDegrees(Vector<float> value) => Map(value, static value => (float)(value * (180 / double.Pi)));
        public static Vector<double> SquareRoot(Vector<double> value) => Map(value, double.Sqrt);
        public static Vector<float> SquareRoot(Vector<float> value) => Map(value, static value => (float)double.Sqrt(value));
        public static Vector<double> Hypot(Vector<double> left, Vector<double> right) => Map(left, right, static (a, b) => double.Hypot(a, b));
        public static Vector<float> Hypot(Vector<float> left, Vector<float> right) => Map(left, right, static (a, b) => (float)double.Hypot(a, b));
        public static Vector<double> FusedMultiplyAdd(Vector<double> left, Vector<double> right, Vector<double> addend) => Map(left, right, addend, static (a, b, c) => a * b + c);
        public static Vector<float> FusedMultiplyAdd(Vector<float> left, Vector<float> right, Vector<float> addend) => Map(left, right, addend, static (a, b, c) => a * b + c);
        public static Vector<double> MultiplyAddEstimate(Vector<double> left, Vector<double> right, Vector<double> addend) => FusedMultiplyAdd(left, right, addend);
        public static Vector<float> MultiplyAddEstimate(Vector<float> left, Vector<float> right, Vector<float> addend) => FusedMultiplyAdd(left, right, addend);
        public static Vector<double> Lerp(Vector<double> left, Vector<double> right, Vector<double> amount) => Map(left, right, amount, static (a, b, c) => a + (b - a) * c);
        public static Vector<float> Lerp(Vector<float> left, Vector<float> right, Vector<float> amount) => Map(left, right, amount, static (a, b, c) => a + (b - a) * c);
        public static (Vector<double> Sin, Vector<double> Cos) SinCos(Vector<double> value) => (Sin(value), Cos(value));
        public static (Vector<float> Sin, Vector<float> Cos) SinCos(Vector<float> value) => (Sin(value), Cos(value));

        public static Vector<double> ConvertToDouble(Vector<long> value) => ConvertVector<long, double>(value);
        public static Vector<double> ConvertToDouble(Vector<ulong> value) => ConvertVector<ulong, double>(value);
        public static Vector<int> ConvertToInt32(Vector<float> value) => ConvertVector<float, int>(value);
        public static Vector<int> ConvertToInt32Native(Vector<float> value) => ConvertToInt32(value);
        public static Vector<long> ConvertToInt64(Vector<double> value) => ConvertVector<double, long>(value);
        public static Vector<long> ConvertToInt64Native(Vector<double> value) => ConvertToInt64(value);
        public static Vector<float> ConvertToSingle(Vector<int> value) => ConvertVector<int, float>(value);
        public static Vector<float> ConvertToSingle(Vector<uint> value) => ConvertVector<uint, float>(value);
        public static Vector<uint> ConvertToUInt32(Vector<float> value) => ConvertVector<float, uint>(value);
        public static Vector<uint> ConvertToUInt32Native(Vector<float> value) => ConvertToUInt32(value);
        public static Vector<ulong> ConvertToUInt64(Vector<double> value) => ConvertVector<double, ulong>(value);
        public static Vector<ulong> ConvertToUInt64Native(Vector<double> value) => ConvertToUInt64(value);

        public static Vector<int> Equals(Vector<int> left, Vector<int> right) => MaskVector<int, int>(left, right, static (a, b) => a == b);
        public static Vector<long> Equals(Vector<long> left, Vector<long> right) => MaskVector<long, long>(left, right, static (a, b) => a == b);
        public static Vector<int> Equals(Vector<float> left, Vector<float> right) => MaskVector<float, int>(left, right, static (a, b) => a == b);
        public static Vector<long> Equals(Vector<double> left, Vector<double> right) => MaskVector<double, long>(left, right, static (a, b) => a == b);
        public static Vector<int> GreaterThan(Vector<int> left, Vector<int> right) => MaskVector<int, int>(left, right, static (a, b) => a > b);
        public static Vector<long> GreaterThan(Vector<long> left, Vector<long> right) => MaskVector<long, long>(left, right, static (a, b) => a > b);
        public static Vector<int> GreaterThan(Vector<float> left, Vector<float> right) => MaskVector<float, int>(left, right, static (a, b) => a > b);
        public static Vector<long> GreaterThan(Vector<double> left, Vector<double> right) => MaskVector<double, long>(left, right, static (a, b) => a > b);
        public static Vector<int> GreaterThanOrEqual(Vector<int> left, Vector<int> right) => MaskVector<int, int>(left, right, static (a, b) => a >= b);
        public static Vector<long> GreaterThanOrEqual(Vector<long> left, Vector<long> right) => MaskVector<long, long>(left, right, static (a, b) => a >= b);
        public static Vector<int> GreaterThanOrEqual(Vector<float> left, Vector<float> right) => MaskVector<float, int>(left, right, static (a, b) => a >= b);
        public static Vector<long> GreaterThanOrEqual(Vector<double> left, Vector<double> right) => MaskVector<double, long>(left, right, static (a, b) => a >= b);
        public static Vector<int> LessThan(Vector<int> left, Vector<int> right) => MaskVector<int, int>(left, right, static (a, b) => a < b);
        public static Vector<long> LessThan(Vector<long> left, Vector<long> right) => MaskVector<long, long>(left, right, static (a, b) => a < b);
        public static Vector<int> LessThan(Vector<float> left, Vector<float> right) => MaskVector<float, int>(left, right, static (a, b) => a < b);
        public static Vector<long> LessThan(Vector<double> left, Vector<double> right) => MaskVector<double, long>(left, right, static (a, b) => a < b);
        public static Vector<int> LessThanOrEqual(Vector<int> left, Vector<int> right) => MaskVector<int, int>(left, right, static (a, b) => a <= b);
        public static Vector<long> LessThanOrEqual(Vector<long> left, Vector<long> right) => MaskVector<long, long>(left, right, static (a, b) => a <= b);
        public static Vector<int> LessThanOrEqual(Vector<float> left, Vector<float> right) => MaskVector<float, int>(left, right, static (a, b) => a <= b);
        public static Vector<long> LessThanOrEqual(Vector<double> left, Vector<double> right) => MaskVector<double, long>(left, right, static (a, b) => a <= b);

        public static Vector<byte> ShiftLeft(Vector<byte> value, int shift) => value << shift;
        public static Vector<sbyte> ShiftLeft(Vector<sbyte> value, int shift) => value << shift;
        public static Vector<short> ShiftLeft(Vector<short> value, int shift) => value << shift;
        public static Vector<ushort> ShiftLeft(Vector<ushort> value, int shift) => value << shift;
        public static Vector<int> ShiftLeft(Vector<int> value, int shift) => value << shift;
        public static Vector<uint> ShiftLeft(Vector<uint> value, int shift) => value << shift;
        public static Vector<long> ShiftLeft(Vector<long> value, int shift) => value << shift;
        public static Vector<ulong> ShiftLeft(Vector<ulong> value, int shift) => value << shift;
        public static Vector<nint> ShiftLeft(Vector<nint> value, int shift) => value << shift;
        public static Vector<nuint> ShiftLeft(Vector<nuint> value, int shift) => value << shift;
        public static Vector<sbyte> ShiftRightArithmetic(Vector<sbyte> value, int shift) => value >> shift;
        public static Vector<short> ShiftRightArithmetic(Vector<short> value, int shift) => value >> shift;
        public static Vector<int> ShiftRightArithmetic(Vector<int> value, int shift) => value >> shift;
        public static Vector<long> ShiftRightArithmetic(Vector<long> value, int shift) => value >> shift;
        public static Vector<nint> ShiftRightArithmetic(Vector<nint> value, int shift) => value >> shift;
        public static Vector<byte> ShiftRightLogical(Vector<byte> value, int shift) => value >>> shift;
        public static Vector<sbyte> ShiftRightLogical(Vector<sbyte> value, int shift) => value >>> shift;
        public static Vector<short> ShiftRightLogical(Vector<short> value, int shift) => value >>> shift;
        public static Vector<ushort> ShiftRightLogical(Vector<ushort> value, int shift) => value >>> shift;
        public static Vector<int> ShiftRightLogical(Vector<int> value, int shift) => value >>> shift;
        public static Vector<uint> ShiftRightLogical(Vector<uint> value, int shift) => value >>> shift;
        public static Vector<long> ShiftRightLogical(Vector<long> value, int shift) => value >>> shift;
        public static Vector<ulong> ShiftRightLogical(Vector<ulong> value, int shift) => value >>> shift;
        public static Vector<nint> ShiftRightLogical(Vector<nint> value, int shift) => value >>> shift;
        public static Vector<nuint> ShiftRightLogical(Vector<nuint> value, int shift) => value >>> shift;

        public static Vector<T> ConcatLowerLower<T>(Vector<T> left, Vector<T> right) => Concat(left, right, 0, 0);
        public static Vector<T> ConcatLowerUpper<T>(Vector<T> left, Vector<T> right) => Concat(left, right, 0, 1);
        public static Vector<T> ConcatUpperLower<T>(Vector<T> left, Vector<T> right) => Concat(left, right, 1, 0);
        public static Vector<T> ConcatUpperUpper<T>(Vector<T> left, Vector<T> right) => Concat(left, right, 1, 1);
        public static Vector<T> ZipLower<T>(Vector<T> left, Vector<T> right) => ZipHalf(left, right, false);
        public static Vector<T> ZipUpper<T>(Vector<T> left, Vector<T> right) => ZipHalf(left, right, true);
        public static (Vector<T> Lower, Vector<T> Upper) Zip<T>(Vector<T> left, Vector<T> right) => (ZipLower(left, right), ZipUpper(left, right));
        public static Vector<T> UnzipEven<T>(Vector<T> left, Vector<T> right) => UnzipHalf(left, right, false);
        public static Vector<T> UnzipOdd<T>(Vector<T> left, Vector<T> right) => UnzipHalf(left, right, true);
        public static (Vector<T> Even, Vector<T> Odd) Unzip<T>(Vector<T> left, Vector<T> right) => (UnzipEven(left, right), UnzipOdd(left, right));

        public static Vector<float> Narrow(Vector<double> left, Vector<double> right) => NarrowPair<double, float>(left, right, false);
        public static Vector<sbyte> Narrow(Vector<short> left, Vector<short> right) => NarrowPair<short, sbyte>(left, right, false);
        public static Vector<short> Narrow(Vector<int> left, Vector<int> right) => NarrowPair<int, short>(left, right, false);
        public static Vector<int> Narrow(Vector<long> left, Vector<long> right) => NarrowPair<long, int>(left, right, false);
        public static Vector<byte> Narrow(Vector<ushort> left, Vector<ushort> right) => NarrowPair<ushort, byte>(left, right, false);
        public static Vector<ushort> Narrow(Vector<uint> left, Vector<uint> right) => NarrowPair<uint, ushort>(left, right, false);
        public static Vector<uint> Narrow(Vector<ulong> left, Vector<ulong> right) => NarrowPair<ulong, uint>(left, right, false);
        public static Vector<float> NarrowWithSaturation(Vector<double> left, Vector<double> right) => NarrowPair<double, float>(left, right, true);
        public static Vector<sbyte> NarrowWithSaturation(Vector<short> left, Vector<short> right) => NarrowPair<short, sbyte>(left, right, true);
        public static Vector<short> NarrowWithSaturation(Vector<int> left, Vector<int> right) => NarrowPair<int, short>(left, right, true);
        public static Vector<int> NarrowWithSaturation(Vector<long> left, Vector<long> right) => NarrowPair<long, int>(left, right, true);
        public static Vector<byte> NarrowWithSaturation(Vector<ushort> left, Vector<ushort> right) => NarrowPair<ushort, byte>(left, right, true);
        public static Vector<ushort> NarrowWithSaturation(Vector<uint> left, Vector<uint> right) => NarrowPair<uint, ushort>(left, right, true);
        public static Vector<uint> NarrowWithSaturation(Vector<ulong> left, Vector<ulong> right) => NarrowPair<ulong, uint>(left, right, true);

        public static void Widen(Vector<byte> source, out Vector<ushort> low, out Vector<ushort> high) => WidenPair(source, out low, out high);
        public static void Widen(Vector<sbyte> source, out Vector<short> low, out Vector<short> high) => WidenPair(source, out low, out high);
        public static void Widen(Vector<short> source, out Vector<int> low, out Vector<int> high) => WidenPair(source, out low, out high);
        public static void Widen(Vector<ushort> source, out Vector<uint> low, out Vector<uint> high) => WidenPair(source, out low, out high);
        public static void Widen(Vector<int> source, out Vector<long> low, out Vector<long> high) => WidenPair(source, out low, out high);
        public static void Widen(Vector<uint> source, out Vector<ulong> low, out Vector<ulong> high) => WidenPair(source, out low, out high);
        public static void Widen(Vector<float> source, out Vector<double> low, out Vector<double> high) => WidenPair(source, out low, out high);
        public static Vector<ushort> WidenLower(Vector<byte> value) => WidenLowerValue<byte, ushort>(value);
        public static Vector<short> WidenLower(Vector<sbyte> value) => WidenLowerValue<sbyte, short>(value);
        public static Vector<int> WidenLower(Vector<short> value) => WidenLowerValue<short, int>(value);
        public static Vector<uint> WidenLower(Vector<ushort> value) => WidenLowerValue<ushort, uint>(value);
        public static Vector<long> WidenLower(Vector<int> value) => WidenLowerValue<int, long>(value);
        public static Vector<ulong> WidenLower(Vector<uint> value) => WidenLowerValue<uint, ulong>(value);
        public static Vector<double> WidenLower(Vector<float> value) => WidenLowerValue<float, double>(value);
        public static Vector<ushort> WidenUpper(Vector<byte> value) => WidenUpperValue<byte, ushort>(value);
        public static Vector<short> WidenUpper(Vector<sbyte> value) => WidenUpperValue<sbyte, short>(value);
        public static Vector<int> WidenUpper(Vector<short> value) => WidenUpperValue<short, int>(value);
        public static Vector<uint> WidenUpper(Vector<ushort> value) => WidenUpperValue<ushort, uint>(value);
        public static Vector<long> WidenUpper(Vector<int> value) => WidenUpperValue<int, long>(value);
        public static Vector<ulong> WidenUpper(Vector<uint> value) => WidenUpperValue<uint, ulong>(value);
        public static Vector<double> WidenUpper(Vector<float> value) => WidenUpperValue<float, double>(value);

#pragma warning disable CS8500
        [Intrinsic]
        public static Vector<T> Load<T>(T* source)
        {
            EnsureSupported<T>();
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, source[index]);
            return result;
        }

        [Intrinsic]
        public static Vector<T> LoadAligned<T>(T* source) => Load(source);

        [Intrinsic]
        public static Vector<T> LoadAlignedNonTemporal<T>(T* source) => LoadAligned(source);

        [Intrinsic]
        public static Vector<T> LoadUnsafe<T>(ref readonly T source)
        {
            EnsureSupported<T>();
            var result = new Vector<T>();
            ref T first = ref Unsafe.AsRef(in source);
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, Unsafe.Add(ref first, index));
            return result;
        }

        [Intrinsic]
        public static Vector<T> LoadUnsafe<T>(ref readonly T source, nuint elementOffset)
        {
            EnsureSupported<T>();
            var result = new Vector<T>();
            ref T first = ref Unsafe.Add(ref Unsafe.AsRef(in source), (nint)elementOffset);
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, Unsafe.Add(ref first, index));
            return result;
        }

        [Intrinsic]
        public static void Store<T>(this Vector<T> source, T* destination)
        {
            EnsureSupported<T>();
            for (var index = 0; index < Vector<T>.Count; index++) destination[index] = source[index];
        }

        [Intrinsic]
        public static void StoreAligned<T>(this Vector<T> source, T* destination) => source.Store(destination);

        [Intrinsic]
        public static void StoreAlignedNonTemporal<T>(this Vector<T> source, T* destination) => source.StoreAligned(destination);

        [Intrinsic]
        public static void StoreUnsafe<T>(this Vector<T> source, ref T destination)
        {
            EnsureSupported<T>();
            for (var index = 0; index < Vector<T>.Count; index++) Unsafe.Add(ref destination, index) = source[index];
        }

        [Intrinsic]
        public static void StoreUnsafe<T>(this Vector<T> source, ref T destination, nuint elementOffset)
        {
            EnsureSupported<T>();
            ref T first = ref Unsafe.Add(ref destination, (nint)elementOffset);
            source.StoreUnsafe(ref first);
        }
#pragma warning restore CS8500

        public static unsafe void Store(this Vector2 source, float* destination)
        {
            destination[0] = source.X;
            destination[1] = source.Y;
        }

        public static unsafe void Store(this Vector3 source, float* destination)
        {
            destination[0] = source.X;
            destination[1] = source.Y;
            destination[2] = source.Z;
        }

        public static unsafe void Store(this Vector4 source, float* destination)
        {
            destination[0] = source.X;
            destination[1] = source.Y;
            destination[2] = source.Z;
            destination[3] = source.W;
        }

        public static unsafe void StoreAligned(this Vector2 source, float* destination) => source.Store(destination);
        public static unsafe void StoreAligned(this Vector3 source, float* destination) => source.Store(destination);
        public static unsafe void StoreAligned(this Vector4 source, float* destination) => source.Store(destination);
        public static unsafe void StoreAlignedNonTemporal(this Vector2 source, float* destination) => source.StoreAligned(destination);
        public static unsafe void StoreAlignedNonTemporal(this Vector3 source, float* destination) => source.StoreAligned(destination);
        public static unsafe void StoreAlignedNonTemporal(this Vector4 source, float* destination) => source.StoreAligned(destination);

        public static void StoreUnsafe(this Vector2 source, ref float destination)
        {
            destination = source.X;
            Unsafe.Add(ref destination, 1) = source.Y;
        }

        public static void StoreUnsafe(this Vector3 source, ref float destination)
        {
            destination = source.X;
            Unsafe.Add(ref destination, 1) = source.Y;
            Unsafe.Add(ref destination, 2) = source.Z;
        }

        public static void StoreUnsafe(this Vector4 source, ref float destination)
        {
            destination = source.X;
            Unsafe.Add(ref destination, 1) = source.Y;
            Unsafe.Add(ref destination, 2) = source.Z;
            Unsafe.Add(ref destination, 3) = source.W;
        }

        public static void StoreUnsafe(this Vector2 source, ref float destination, nuint elementOffset)
        {
            ref float target = ref Unsafe.Add(ref destination, (nint)elementOffset);
            source.StoreUnsafe(ref target);
        }

        public static void StoreUnsafe(this Vector3 source, ref float destination, nuint elementOffset)
        {
            ref float target = ref Unsafe.Add(ref destination, (nint)elementOffset);
            source.StoreUnsafe(ref target);
        }

        public static void StoreUnsafe(this Vector4 source, ref float destination, nuint elementOffset)
        {
            ref float target = ref Unsafe.Add(ref destination, (nint)elementOffset);
            source.StoreUnsafe(ref target);
        }

        private static Vector<T> Map<T>(Vector<T> value, Func<T, T> operation) => VectorScalar<T>.Map(value, operation);
        private static void EnsureSupported<T>()
        {
            if (!Vector<T>.IsSupported) throw new NotSupportedException();
        }

        private static Vector<T> Map<T>(Vector<T> left, Vector<T> right, Func<T, T, T> operation) => VectorScalar<T>.Map(left, right, operation);
        private static Vector<T> Map<T>(Vector<T> left, Vector<T> right, Vector<T> third, Func<T, T, T, T> operation)
        {
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, operation(left[index], right[index], third[index]));
            return result;
        }
        private static Vector<T> Select<TMask, T>(Vector<TMask> condition, Vector<T> left, Vector<T> right)
        {
            var result = new Vector<T>();
            for (var index = 0; index < Vector<T>.Count; index++) result.SetElementUnsafe(index, object.Equals(condition[index], default(TMask)) ? right[index] : left[index]);
            return result;
        }
        private static Vector<T> Compare<T>(Vector<T> left, Vector<T> right, Func<T, T, bool> comparison) => Map(left, right, (a, b) => comparison(a, b) ? VectorScalar<T>.AllBitsSet : default!);
        private static bool All<T>(Vector<T> left, Vector<T> right, Func<T, T, bool> comparison)
        {
            for (var index = 0; index < Vector<T>.Count; index++) if (!comparison(left[index], right[index])) return false;
            return true;
        }
        private static bool Any<T>(Vector<T> left, Vector<T> right, Func<T, T, bool> comparison)
        {
            for (var index = 0; index < Vector<T>.Count; index++) if (comparison(left[index], right[index])) return true;
            return false;
        }
        private static int CountWhere<T>(Vector<T> vector, T value) { var count = 0; for (var index = 0; index < Vector<T>.Count; index++) if (VectorScalar<T>.Equals(vector[index], value)) count++; return count; }
        private static int Find<T>(Vector<T> vector, T value, bool reverse) { if (reverse) { for (var index = Vector<T>.Count - 1; index >= 0; index--) if (VectorScalar<T>.Equals(vector[index], value)) return index; } else { for (var index = 0; index < Vector<T>.Count; index++) if (VectorScalar<T>.Equals(vector[index], value)) return index; } return -1; }
        private static Vector<TTo> ConvertVector<TFrom, TTo>(Vector<TFrom> value)
        {
            var result = new Vector<TTo>();
            for (var index = 0; index < Vector<TTo>.Count && index < Vector<TFrom>.Count; index++) result.SetElementUnsafe(index, VectorScalar<TFrom>.ConvertValue<TTo>(value[index]));
            return result;
        }
        private static Vector<TTo> MaskVector<TFrom, TTo>(Vector<TFrom> left, Vector<TFrom> right, Func<TFrom, TFrom, bool> comparison)
        {
            var result = new Vector<TTo>();
            for (var index = 0; index < Vector<TTo>.Count && index < Vector<TFrom>.Count; index++) result.SetElementUnsafe(index, comparison(left[index], right[index]) ? VectorScalar<TTo>.AllBitsSet : VectorScalar<TTo>.Zero);
            return result;
        }

        private static Vector<T> Concat<T>(Vector<T> left, Vector<T> right, int leftHalf, int rightHalf)
        {
            var result = new Vector<T>();
            var half = Vector<T>.Count / 2;
            for (var index = 0; index < half; index++) result.SetElementUnsafe(index, (leftHalf == 0 ? left : right)[index]);
            for (var index = 0; index < half; index++) result.SetElementUnsafe(half + index, (rightHalf == 0 ? left : right)[index]);
            return result;
        }
        private static Vector<T> ZipHalf<T>(Vector<T> left, Vector<T> right, bool upper)
        {
            var result = new Vector<T>();
            var half = Vector<T>.Count / 2;
            var start = upper ? half : 0;
            for (var index = 0; index < half; index++)
            {
                result.SetElementUnsafe(index * 2, left[start + index]);
                result.SetElementUnsafe(index * 2 + 1, right[start + index]);
            }
            return result;
        }
        private static Vector<T> UnzipHalf<T>(Vector<T> left, Vector<T> right, bool odd)
        {
            var result = new Vector<T>();
            var half = Vector<T>.Count / 2;
            for (var index = 0; index < half; index++) result.SetElementUnsafe(index, (odd ? right : left)[index * 2 + (odd ? 1 : 0)]);
            for (var index = 0; index < half; index++) result.SetElementUnsafe(half + index, (odd ? right : left)[index * 2 + (odd ? 1 : 0)]);
            return result;
        }
        private static Vector<TTo> NarrowPair<TFrom, TTo>(Vector<TFrom> left, Vector<TFrom> right, bool saturating)
        {
            var result = new Vector<TTo>();
            var half = Vector<TTo>.Count / 2;
            for (var index = 0; index < half; index++) result.SetElementUnsafe(index, VectorScalar<TFrom>.ConvertValue<TTo>(left[index]));
            for (var index = 0; index < half; index++) result.SetElementUnsafe(half + index, VectorScalar<TFrom>.ConvertValue<TTo>(right[index]));
            return result;
        }
        private static void WidenPair<TFrom, TTo>(Vector<TFrom> source, out Vector<TTo> low, out Vector<TTo> high)
        {
            low = WidenLowerValue<TFrom, TTo>(source);
            high = WidenUpperValue<TFrom, TTo>(source);
        }
        private static Vector<TTo> WidenLowerValue<TFrom, TTo>(Vector<TFrom> source)
        {
            var result = new Vector<TTo>();
            for (var index = 0; index < Vector<TTo>.Count; index++) result.SetElementUnsafe(index, VectorScalar<TFrom>.ConvertValue<TTo>(source[index]));
            return result;
        }
        private static Vector<TTo> WidenUpperValue<TFrom, TTo>(Vector<TFrom> source)
        {
            var result = new Vector<TTo>();
            var half = Vector<TFrom>.Count / 2;
            for (var index = 0; index < Vector<TTo>.Count; index++) result.SetElementUnsafe(index, VectorScalar<TFrom>.ConvertValue<TTo>(source[half + index]));
            return result;
        }
    }
}
