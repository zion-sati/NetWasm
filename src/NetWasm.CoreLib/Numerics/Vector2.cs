// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Numerics
{
    /// <summary>Represents a vector with two single-precision floating-point values.</summary>
    public partial struct Vector2 : IEquatable<Vector2>, IFormattable
    {
        private const int ElementCount = 2;
        private const int Alignment = 8;

        public float X;
        public float Y;

        public Vector2(float value) { X = value; Y = value; }
        public Vector2(float x, float y) { X = x; Y = y; }
        public Vector2(ReadOnlySpan<float> values)
        {
            if (values.Length < ElementCount) throw new ArgumentOutOfRangeException(nameof(values));
            X = values[0];
            Y = values[1];
        }

        private static float Mask(bool value) => value ? BitConverter.Int32BitsToSingle(-1) : 0f;
        private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
        private static float FromBits(int value) => BitConverter.Int32BitsToSingle(value);
        private static float And(float left, float right) => FromBits(Bits(left) & Bits(right));
        private static float Or(float left, float right) => FromBits(Bits(left) | Bits(right));
        private static float XorScalar(float left, float right) => FromBits(Bits(left) ^ Bits(right));
        private static float AndNotScalar(float left, float right) => FromBits(Bits(left) & ~Bits(right));
        private static float Select(float condition, float left, float right) => Or(And(condition, left), AndNotScalar(right, condition));
        private static float ShiftLeft(float value, int amount) => FromBits(Bits(value) << amount);
        private static float ShiftRight(float value, int amount) => FromBits(Bits(value) >> amount);
        private static float ShiftRightLogical(float value, int amount) => FromBits((int)((uint)Bits(value) >> amount));

        public static Vector2 AllBitsSet
        {
            get => new(FromBits(-1));
        }

        public static Vector2 E
        {
            get => new(float.E);
        }

        public static Vector2 Epsilon
        {
            get => new(float.Epsilon);
        }

        public static Vector2 NaN
        {
            get => new(float.NaN);
        }

        public static Vector2 NegativeInfinity
        {
            get => new(float.NegativeInfinity);
        }

        public static Vector2 NegativeZero
        {
            get => new(float.NegativeZero);
        }

        public static Vector2 One
        {
            get => new(1f);
        }

        public static Vector2 Pi
        {
            get => new(float.Pi);
        }

        public static Vector2 PositiveInfinity
        {
            get => new(float.PositiveInfinity);
        }

        public static Vector2 Tau
        {
            get => new(float.Tau);
        }

        public static Vector2 UnitX
        {
            get => new(1f, 0f);
        }

        public static Vector2 UnitY
        {
            get => new(0f, 1f);
        }

        public static Vector2 Zero
        {
            get => default;
        }

        public float this[int index]
        {
            readonly get => index switch
            {
                0 => X,
                1 => Y,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static Vector2 operator +(Vector2 left, Vector2 right) => new(left.X + right.X, left.Y + right.Y);
        public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.X - right.X, left.Y - right.Y);
        public static Vector2 operator -(Vector2 value) => new(-value.X, -value.Y);
        public static Vector2 operator +(Vector2 value) => value;
        public static Vector2 operator *(Vector2 left, Vector2 right) => new(left.X * right.X, left.Y * right.Y);
        public static Vector2 operator *(Vector2 left, float right) => new(left.X * right, left.Y * right);
        public static Vector2 operator *(float left, Vector2 right) => right * left;
        public static Vector2 operator /(Vector2 left, Vector2 right) => new(left.X / right.X, left.Y / right.Y);
        public static Vector2 operator /(Vector2 left, float right) => new(left.X / right, left.Y / right);
        public static bool operator ==(Vector2 left, Vector2 right) => left.X == right.X && left.Y == right.Y;
        public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);
        public static Vector2 operator &(Vector2 left, Vector2 right) => BitwiseAnd(left, right);
        public static Vector2 operator |(Vector2 left, Vector2 right) => BitwiseOr(left, right);
        public static Vector2 operator ^(Vector2 left, Vector2 right) => Xor(left, right);
        public static Vector2 operator ~(Vector2 value) => OnesComplement(value);
        public static Vector2 operator <<(Vector2 value, int shiftAmount) => new(ShiftLeft(value.X, shiftAmount), ShiftLeft(value.Y, shiftAmount));
        public static Vector2 operator >>(Vector2 value, int shiftAmount) => new(ShiftRight(value.X, shiftAmount), ShiftRight(value.Y, shiftAmount));
        public static Vector2 operator >>>(Vector2 value, int shiftAmount) => new(ShiftRightLogical(value.X, shiftAmount), ShiftRightLogical(value.Y, shiftAmount));

        public static Vector2 Abs(Vector2 value) => new(float.Abs(value.X), float.Abs(value.Y));
        public static Vector2 Add(Vector2 left, Vector2 right) => left + right;
        public static bool All(Vector2 vector, float value) => vector.X == value && vector.Y == value;
        public static bool AllWhereAllBitsSet(Vector2 vector) => Bits(vector.X) == -1 && Bits(vector.Y) == -1;
        public static Vector2 AndNot(Vector2 left, Vector2 right) => new(AndNotScalar(left.X, right.X), AndNotScalar(left.Y, right.Y));
        public static bool Any(Vector2 vector, float value) => vector.X == value || vector.Y == value;
        public static bool AnyWhereAllBitsSet(Vector2 vector) => Bits(vector.X) == -1 || Bits(vector.Y) == -1;
        public static Vector2 BitwiseAnd(Vector2 left, Vector2 right) => new(And(left.X, right.X), And(left.Y, right.Y));
        public static Vector2 BitwiseOr(Vector2 left, Vector2 right) => new(Or(left.X, right.X), Or(left.Y, right.Y));
        public static Vector2 Clamp(Vector2 value1, Vector2 min, Vector2 max) => new(float.Clamp(value1.X, min.X, max.X), float.Clamp(value1.Y, min.Y, max.Y));
        public static Vector2 ClampNative(Vector2 value1, Vector2 min, Vector2 max) => new(float.Max(float.Min(value1.X, max.X), min.X), float.Max(float.Min(value1.Y, max.Y), min.Y));
        public static Vector2 ConditionalSelect(Vector2 condition, Vector2 left, Vector2 right) => new(Select(condition.X, left.X, right.X), Select(condition.Y, left.Y, right.Y));
        public static Vector2 CopySign(Vector2 value, Vector2 sign) => new(float.CopySign(value.X, sign.X), float.CopySign(value.Y, sign.Y));
        public static Vector2 Cos(Vector2 vector) => new(float.Cos(vector.X), float.Cos(vector.Y));
        public static int Count(Vector2 vector, float value) => (vector.X == value ? 1 : 0) + (vector.Y == value ? 1 : 0);
        public static int CountWhereAllBitsSet(Vector2 vector) => (Bits(vector.X) == -1 ? 1 : 0) + (Bits(vector.Y) == -1 ? 1 : 0);
        public static Vector2 Create(float value) => new(value);
        public static Vector2 Create(float x, float y) => new(x, y);
        public static Vector2 Create(ReadOnlySpan<float> values) => new(values);
        public static Vector2 CreateScalar(float x) => new(x, 0f);
        public static unsafe Vector2 CreateScalarUnsafe(float x) => new(x, 0f);
        public static Vector2 DegreesToRadians(Vector2 degrees) => degrees * (float.Pi / 180f);
        public static float Distance(Vector2 value1, Vector2 value2) => (value1 - value2).Length();
        public static float DistanceSquared(Vector2 value1, Vector2 value2) => (value1 - value2).LengthSquared();
        public static Vector2 Divide(Vector2 left, Vector2 right) => left / right;
        public static Vector2 Divide(Vector2 left, float divisor) => left / divisor;
        public static float Dot(Vector2 value1, Vector2 value2) => value1.X * value2.X + value1.Y * value2.Y;
        public static float Cross(Vector2 value1, Vector2 value2) => value1.X * value2.Y - value1.Y * value2.X;
        public static Vector2 Equals(Vector2 left, Vector2 right) => new(Mask(left.X == right.X), Mask(left.Y == right.Y));
        public static bool EqualsAll(Vector2 left, Vector2 right) => left == right;
        public static bool EqualsAny(Vector2 left, Vector2 right) => left.X == right.X || left.Y == right.Y;
        public static Vector2 Exp(Vector2 vector) => new(float.Exp(vector.X), float.Exp(vector.Y));
        public static Vector2 FusedMultiplyAdd(Vector2 left, Vector2 right, Vector2 addend) => new(float.FusedMultiplyAdd(left.X, right.X, addend.X), float.FusedMultiplyAdd(left.Y, right.Y, addend.Y));
        public static Vector2 GreaterThan(Vector2 left, Vector2 right) => new(Mask(left.X > right.X), Mask(left.Y > right.Y));
        public static bool GreaterThanAll(Vector2 left, Vector2 right) => left.X > right.X && left.Y > right.Y;
        public static bool GreaterThanAny(Vector2 left, Vector2 right) => left.X > right.X || left.Y > right.Y;
        public static Vector2 GreaterThanOrEqual(Vector2 left, Vector2 right) => new(Mask(left.X >= right.X), Mask(left.Y >= right.Y));
        public static bool GreaterThanOrEqualAll(Vector2 left, Vector2 right) => left.X >= right.X && left.Y >= right.Y;
        public static bool GreaterThanOrEqualAny(Vector2 left, Vector2 right) => left.X >= right.X || left.Y >= right.Y;
        public static Vector2 Hypot(Vector2 x, Vector2 y) => new(float.Hypot(x.X, y.X), float.Hypot(x.Y, y.Y));
        public static int IndexOf(Vector2 vector, float value) => vector.X == value ? 0 : vector.Y == value ? 1 : -1;
        public static int IndexOfWhereAllBitsSet(Vector2 vector) => Bits(vector.X) == -1 ? 0 : Bits(vector.Y) == -1 ? 1 : -1;
        public static Vector2 IsEvenInteger(Vector2 vector) => new(Mask(float.IsEvenInteger(vector.X)), Mask(float.IsEvenInteger(vector.Y)));
        public static Vector2 IsFinite(Vector2 vector) => new(Mask(float.IsFinite(vector.X)), Mask(float.IsFinite(vector.Y)));
        public static Vector2 IsInfinity(Vector2 vector) => new(Mask(float.IsInfinity(vector.X)), Mask(float.IsInfinity(vector.Y)));
        public static Vector2 IsInteger(Vector2 vector) => new(Mask(float.IsInteger(vector.X)), Mask(float.IsInteger(vector.Y)));
        public static Vector2 IsNaN(Vector2 vector) => new(Mask(float.IsNaN(vector.X)), Mask(float.IsNaN(vector.Y)));
        public static Vector2 IsNegative(Vector2 vector) => new(Mask(float.IsNegative(vector.X)), Mask(float.IsNegative(vector.Y)));
        public static Vector2 IsNegativeInfinity(Vector2 vector) => new(Mask(float.IsNegativeInfinity(vector.X)), Mask(float.IsNegativeInfinity(vector.Y)));
        public static Vector2 IsNormal(Vector2 vector) => new(Mask(float.IsNormal(vector.X)), Mask(float.IsNormal(vector.Y)));
        public static Vector2 IsOddInteger(Vector2 vector) => new(Mask(float.IsOddInteger(vector.X)), Mask(float.IsOddInteger(vector.Y)));
        public static Vector2 IsPositive(Vector2 vector) => new(Mask(float.IsPositive(vector.X)), Mask(float.IsPositive(vector.Y)));
        public static Vector2 IsPositiveInfinity(Vector2 vector) => new(Mask(float.IsPositiveInfinity(vector.X)), Mask(float.IsPositiveInfinity(vector.Y)));
        public static Vector2 IsSubnormal(Vector2 vector) => new(Mask(float.IsSubnormal(vector.X)), Mask(float.IsSubnormal(vector.Y)));
        public static Vector2 IsZero(Vector2 vector) => new(Mask(float.IsZero(vector.X)), Mask(float.IsZero(vector.Y)));
        public static int LastIndexOf(Vector2 vector, float value) => vector.Y == value ? 1 : vector.X == value ? 0 : -1;
        public static int LastIndexOfWhereAllBitsSet(Vector2 vector) => Bits(vector.Y) == -1 ? 1 : Bits(vector.X) == -1 ? 0 : -1;
        public static Vector2 Lerp(Vector2 value1, Vector2 value2, float amount) => value1 + (value2 - value1) * amount;
        public static Vector2 Lerp(Vector2 value1, Vector2 value2, Vector2 amount) => value1 + (value2 - value1) * amount;
        public static Vector2 LessThan(Vector2 left, Vector2 right) => new(Mask(left.X < right.X), Mask(left.Y < right.Y));
        public static bool LessThanAll(Vector2 left, Vector2 right) => left.X < right.X && left.Y < right.Y;
        public static bool LessThanAny(Vector2 left, Vector2 right) => left.X < right.X || left.Y < right.Y;
        public static Vector2 LessThanOrEqual(Vector2 left, Vector2 right) => new(Mask(left.X <= right.X), Mask(left.Y <= right.Y));
        public static bool LessThanOrEqualAll(Vector2 left, Vector2 right) => left.X <= right.X && left.Y <= right.Y;
        public static bool LessThanOrEqualAny(Vector2 left, Vector2 right) => left.X <= right.X || left.Y <= right.Y;
        public static unsafe Vector2 Load(float* source) => new(source[0], source[1]);
        public static unsafe Vector2 LoadAligned(float* source)
        {
            if ((nuint)source % Alignment != 0) throw new AccessViolationException();
            return Load(source);
        }
        public static unsafe Vector2 LoadAlignedNonTemporal(float* source) => LoadAligned(source);
        public static Vector2 LoadUnsafe(ref readonly float source) => new(source, Unsafe.Add(ref Unsafe.AsRef(in source), 1));
        public static Vector2 LoadUnsafe(ref readonly float source, nuint elementOffset) => new(Unsafe.Add(ref Unsafe.AsRef(in source), (nint)elementOffset), Unsafe.Add(ref Unsafe.AsRef(in source), (nint)elementOffset + 1));
        public static Vector2 Log(Vector2 vector) => new(float.Log(vector.X), float.Log(vector.Y));
        public static Vector2 Log2(Vector2 vector) => new(float.Log2(vector.X), float.Log2(vector.Y));
        public static Vector2 Max(Vector2 value1, Vector2 value2) => new(float.Max(value1.X, value2.X), float.Max(value1.Y, value2.Y));
        public static Vector2 MaxMagnitude(Vector2 value1, Vector2 value2) => new(float.MaxMagnitude(value1.X, value2.X), float.MaxMagnitude(value1.Y, value2.Y));
        public static Vector2 MaxMagnitudeNumber(Vector2 value1, Vector2 value2) => new(float.MaxMagnitudeNumber(value1.X, value2.X), float.MaxMagnitudeNumber(value1.Y, value2.Y));
        public static Vector2 MaxNative(Vector2 value1, Vector2 value2) => Max(value1, value2);
        public static Vector2 MaxNumber(Vector2 value1, Vector2 value2) => new(float.Max(value1.X, value2.X), float.Max(value1.Y, value2.Y));
        public static Vector2 Min(Vector2 value1, Vector2 value2) => new(float.Min(value1.X, value2.X), float.Min(value1.Y, value2.Y));
        public static Vector2 MinMagnitude(Vector2 value1, Vector2 value2) => new(float.MinMagnitude(value1.X, value2.X), float.MinMagnitude(value1.Y, value2.Y));
        public static Vector2 MinMagnitudeNumber(Vector2 value1, Vector2 value2) => new(float.MinMagnitudeNumber(value1.X, value2.X), float.MinMagnitudeNumber(value1.Y, value2.Y));
        public static Vector2 MinNative(Vector2 value1, Vector2 value2) => Min(value1, value2);
        public static Vector2 MinNumber(Vector2 value1, Vector2 value2) => new(float.Min(value1.X, value2.X), float.Min(value1.Y, value2.Y));
        public static Vector2 Multiply(Vector2 left, Vector2 right) => left * right;
        public static Vector2 Multiply(Vector2 left, float right) => left * right;
        public static Vector2 Multiply(float left, Vector2 right) => left * right;
        public static Vector2 MultiplyAddEstimate(Vector2 left, Vector2 right, Vector2 addend) => left * right + addend;
        public static Vector2 Negate(Vector2 value) => -value;
        public static bool None(Vector2 vector, float value) => vector.X != value && vector.Y != value;
        public static bool NoneWhereAllBitsSet(Vector2 vector) => Bits(vector.X) != -1 && Bits(vector.Y) != -1;
        public static Vector2 Normalize(Vector2 value) => value / value.Length();
        public static Vector2 OnesComplement(Vector2 value) => new(FromBits(~Bits(value.X)), FromBits(~Bits(value.Y)));
        public static Vector2 RadiansToDegrees(Vector2 radians) => radians * (180f / float.Pi);
        public static Vector2 Round(Vector2 vector) => new((float)Math.Round(vector.X), (float)Math.Round(vector.Y));
        public static Vector2 Round(Vector2 vector, MidpointRounding mode) => new((float)Math.Round(vector.X, mode), (float)Math.Round(vector.Y, mode));
        public static Vector2 Reflect(Vector2 vector, Vector2 normal) => vector - (2f * Dot(vector, normal)) * normal;
        public static Vector2 Shuffle(Vector2 vector, byte xIndex, byte yIndex) => new(xIndex < ElementCount ? vector[xIndex] : 0f, yIndex < ElementCount ? vector[yIndex] : 0f);
        public static Vector2 Sin(Vector2 vector) => new(float.Sin(vector.X), float.Sin(vector.Y));
        public static (Vector2 Sin, Vector2 Cos) SinCos(Vector2 vector) => (Sin(vector), Cos(vector));
        public static Vector2 SquareRoot(Vector2 value) => new(float.Sqrt(value.X), float.Sqrt(value.Y));
        public static Vector2 Subtract(Vector2 left, Vector2 right) => left - right;
        public static float Sum(Vector2 value) => value.X + value.Y;

        /// <summary>Transforms a vector by a specified 3x2 matrix.</summary>
        public static Vector2 Transform(Vector2 position, Matrix3x2 matrix) => new(
            position.X * matrix.M11 + position.Y * matrix.M21 + matrix.M31,
            position.X * matrix.M12 + position.Y * matrix.M22 + matrix.M32);

        public static Vector2 Transform(Vector2 position, Matrix4x4 matrix) => new(
            position.X * matrix.M11 + position.Y * matrix.M21 + matrix.M41,
            position.X * matrix.M12 + position.Y * matrix.M22 + matrix.M42);

        /// <summary>Transforms a vector by the specified Quaternion rotation value.</summary>
        public static Vector2 Transform(Vector2 value, Quaternion rotation)
        {
            var transformed = Vector4.Transform(new Vector4(value, 0f, 1f), rotation);
            return new(transformed.X, transformed.Y);
        }

        /// <summary>Transforms a vector normal by the given 3x2 matrix.</summary>
        public static Vector2 TransformNormal(Vector2 normal, Matrix3x2 matrix) => new(
            normal.X * matrix.M11 + normal.Y * matrix.M21,
            normal.X * matrix.M12 + normal.Y * matrix.M22);

        public static Vector2 TransformNormal(Vector2 normal, Matrix4x4 matrix) => new(
            normal.X * matrix.M11 + normal.Y * matrix.M21,
            normal.X * matrix.M12 + normal.Y * matrix.M22);

        public static Vector2 Truncate(Vector2 vector) => new(float.Truncate(vector.X), float.Truncate(vector.Y));
        public static Vector2 Xor(Vector2 left, Vector2 right) => new(XorScalar(left.X, right.X), XorScalar(left.Y, right.Y));

        public readonly void CopyTo(float[] array)
        {
            if (array.Length < ElementCount) throw new ArgumentException();
            array[0] = X; array[1] = Y;
        }
        public readonly void CopyTo(float[] array, int index)
        {
            if ((uint)index >= (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < ElementCount) throw new ArgumentException();
            array[index] = X; array[index + 1] = Y;
        }
        public readonly void CopyTo(Span<float> destination)
        {
            if (destination.Length < ElementCount) throw new ArgumentException();
            destination[0] = X; destination[1] = Y;
        }
        public readonly bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < ElementCount) return false;
            destination[0] = X; destination[1] = Y;
            return true;
        }

        public readonly bool Equals(Vector2 other) => X == other.X && Y == other.Y;
        public override readonly bool Equals(object? obj) => obj is Vector2 other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y);
        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => X * X + Y * Y;
        public override readonly string ToString() => ToString("G", null);
        public readonly string ToString(string? format) => ToString(format, null);
        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            var separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            var handler = new DefaultInterpolatedStringHandler(3 + separator.Length, 2, formatProvider);
            handler.AppendLiteral("<"); handler.AppendFormatted(X, format); handler.AppendLiteral(separator); handler.AppendLiteral(" "); handler.AppendFormatted(Y, format); handler.AppendLiteral(">");
            return handler.ToStringAndClear();
        }
    }
}
