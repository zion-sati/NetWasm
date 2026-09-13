// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Numerics
{
    /// <summary>Represents a vector with three single-precision floating-point values.</summary>
    public partial struct Vector3 : IEquatable<Vector3>, IFormattable
    {
        private const int ElementCount = 3;
        private const int Alignment = 4;

        public float X;
        public float Y;
        public float Z;

        public Vector3(Vector2 value, float z) { X = value.X; Y = value.Y; Z = z; }
        public Vector3(float value) { X = value; Y = value; Z = value; }
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public Vector3(ReadOnlySpan<float> values)
        {
            if (values.Length < ElementCount) throw new ArgumentOutOfRangeException(nameof(values));
            X = values[0]; Y = values[1]; Z = values[2];
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

        public static Vector3 AllBitsSet
        {
            get => new(FromBits(-1));
        }

        public static Vector3 E
        {
            get => new(float.E);
        }

        public static Vector3 Epsilon
        {
            get => new(float.Epsilon);
        }

        public static Vector3 NaN
        {
            get => new(float.NaN);
        }

        public static Vector3 NegativeInfinity
        {
            get => new(float.NegativeInfinity);
        }

        public static Vector3 NegativeZero
        {
            get => new(float.NegativeZero);
        }

        public static Vector3 One
        {
            get => new(1f);
        }

        public static Vector3 Pi
        {
            get => new(float.Pi);
        }

        public static Vector3 PositiveInfinity
        {
            get => new(float.PositiveInfinity);
        }

        public static Vector3 Tau
        {
            get => new(float.Tau);
        }

        public static Vector3 UnitX
        {
            get => new(1f, 0f, 0f);
        }

        public static Vector3 UnitY
        {
            get => new(0f, 1f, 0f);
        }

        public static Vector3 UnitZ
        {
            get => new(0f, 0f, 1f);
        }

        public static Vector3 Zero
        {
            get => default;
        }

        public float this[int index]
        {
            readonly get => index switch
            {
                0 => X,
                1 => Y,
                2 => Z,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static Vector3 operator +(Vector3 left, Vector3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Vector3 operator -(Vector3 left, Vector3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Vector3 operator -(Vector3 value) => new(-value.X, -value.Y, -value.Z);
        public static Vector3 operator +(Vector3 value) => value;
        public static Vector3 operator *(Vector3 left, Vector3 right) => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        public static Vector3 operator *(Vector3 left, float right) => new(left.X * right, left.Y * right, left.Z * right);
        public static Vector3 operator *(float left, Vector3 right) => right * left;
        public static Vector3 operator /(Vector3 left, Vector3 right) => new(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
        public static Vector3 operator /(Vector3 left, float right) => new(left.X / right, left.Y / right, left.Z / right);
        public static bool operator ==(Vector3 left, Vector3 right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z;
        public static bool operator !=(Vector3 left, Vector3 right) => !(left == right);
        public static Vector3 operator &(Vector3 left, Vector3 right) => BitwiseAnd(left, right);
        public static Vector3 operator |(Vector3 left, Vector3 right) => BitwiseOr(left, right);
        public static Vector3 operator ^(Vector3 left, Vector3 right) => Xor(left, right);
        public static Vector3 operator ~(Vector3 value) => OnesComplement(value);
        public static Vector3 operator <<(Vector3 value, int shiftAmount) => new(ShiftLeft(value.X, shiftAmount), ShiftLeft(value.Y, shiftAmount), ShiftLeft(value.Z, shiftAmount));
        public static Vector3 operator >>(Vector3 value, int shiftAmount) => new(ShiftRight(value.X, shiftAmount), ShiftRight(value.Y, shiftAmount), ShiftRight(value.Z, shiftAmount));
        public static Vector3 operator >>>(Vector3 value, int shiftAmount) => new(ShiftRightLogical(value.X, shiftAmount), ShiftRightLogical(value.Y, shiftAmount), ShiftRightLogical(value.Z, shiftAmount));

        public static Vector3 Abs(Vector3 value) => new(float.Abs(value.X), float.Abs(value.Y), float.Abs(value.Z));
        public static Vector3 Add(Vector3 left, Vector3 right) => left + right;
        public static bool All(Vector3 vector, float value) => vector.X == value && vector.Y == value && vector.Z == value;
        public static bool AllWhereAllBitsSet(Vector3 vector) => Bits(vector.X) == -1 && Bits(vector.Y) == -1 && Bits(vector.Z) == -1;
        public static Vector3 AndNot(Vector3 left, Vector3 right) => new(AndNotScalar(left.X, right.X), AndNotScalar(left.Y, right.Y), AndNotScalar(left.Z, right.Z));
        public static bool Any(Vector3 vector, float value) => vector.X == value || vector.Y == value || vector.Z == value;
        public static bool AnyWhereAllBitsSet(Vector3 vector) => Bits(vector.X) == -1 || Bits(vector.Y) == -1 || Bits(vector.Z) == -1;
        public static Vector3 BitwiseAnd(Vector3 left, Vector3 right) => new(And(left.X, right.X), And(left.Y, right.Y), And(left.Z, right.Z));
        public static Vector3 BitwiseOr(Vector3 left, Vector3 right) => new(Or(left.X, right.X), Or(left.Y, right.Y), Or(left.Z, right.Z));
        public static Vector3 Clamp(Vector3 value1, Vector3 min, Vector3 max) => new(float.Clamp(value1.X, min.X, max.X), float.Clamp(value1.Y, min.Y, max.Y), float.Clamp(value1.Z, min.Z, max.Z));
        public static Vector3 ClampNative(Vector3 value1, Vector3 min, Vector3 max) => new(float.Max(float.Min(value1.X, max.X), min.X), float.Max(float.Min(value1.Y, max.Y), min.Y), float.Max(float.Min(value1.Z, max.Z), min.Z));
        public static Vector3 ConditionalSelect(Vector3 condition, Vector3 left, Vector3 right) => new(Select(condition.X, left.X, right.X), Select(condition.Y, left.Y, right.Y), Select(condition.Z, left.Z, right.Z));
        public static Vector3 CopySign(Vector3 value, Vector3 sign) => new(float.CopySign(value.X, sign.X), float.CopySign(value.Y, sign.Y), float.CopySign(value.Z, sign.Z));
        public static Vector3 Cos(Vector3 vector) => new(float.Cos(vector.X), float.Cos(vector.Y), float.Cos(vector.Z));
        public static int Count(Vector3 vector, float value) => (vector.X == value ? 1 : 0) + (vector.Y == value ? 1 : 0) + (vector.Z == value ? 1 : 0);
        public static int CountWhereAllBitsSet(Vector3 vector) => (Bits(vector.X) == -1 ? 1 : 0) + (Bits(vector.Y) == -1 ? 1 : 0) + (Bits(vector.Z) == -1 ? 1 : 0);
        public static Vector3 Create(float value) => new(value);
        public static Vector3 Create(Vector2 vector, float z) => new(vector, z);
        public static Vector3 Create(float x, float y, float z) => new(x, y, z);
        public static Vector3 Create(ReadOnlySpan<float> values) => new(values);
        public static Vector3 CreateScalar(float x) => new(x, 0f, 0f);
        public static unsafe Vector3 CreateScalarUnsafe(float x) => new(x, 0f, 0f);
        public static Vector3 Cross(Vector3 vector1, Vector3 vector2) => new(vector1.Y * vector2.Z - vector1.Z * vector2.Y, vector1.Z * vector2.X - vector1.X * vector2.Z, vector1.X * vector2.Y - vector1.Y * vector2.X);
        public static Vector3 DegreesToRadians(Vector3 degrees) => degrees * (float.Pi / 180f);
        public static float Distance(Vector3 value1, Vector3 value2) => (value1 - value2).Length();
        public static float DistanceSquared(Vector3 value1, Vector3 value2) => (value1 - value2).LengthSquared();
        public static Vector3 Divide(Vector3 left, Vector3 right) => left / right;
        public static Vector3 Divide(Vector3 left, float divisor) => left / divisor;
        public static float Dot(Vector3 vector1, Vector3 vector2) => vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z;
        public static Vector3 Equals(Vector3 left, Vector3 right) => new(Mask(left.X == right.X), Mask(left.Y == right.Y), Mask(left.Z == right.Z));
        public static bool EqualsAll(Vector3 left, Vector3 right) => left == right;
        public static bool EqualsAny(Vector3 left, Vector3 right) => left.X == right.X || left.Y == right.Y || left.Z == right.Z;
        public static Vector3 Exp(Vector3 vector) => new(float.Exp(vector.X), float.Exp(vector.Y), float.Exp(vector.Z));
        public static Vector3 FusedMultiplyAdd(Vector3 left, Vector3 right, Vector3 addend) => new(float.FusedMultiplyAdd(left.X, right.X, addend.X), float.FusedMultiplyAdd(left.Y, right.Y, addend.Y), float.FusedMultiplyAdd(left.Z, right.Z, addend.Z));
        public static Vector3 GreaterThan(Vector3 left, Vector3 right) => new(Mask(left.X > right.X), Mask(left.Y > right.Y), Mask(left.Z > right.Z));
        public static bool GreaterThanAll(Vector3 left, Vector3 right) => left.X > right.X && left.Y > right.Y && left.Z > right.Z;
        public static bool GreaterThanAny(Vector3 left, Vector3 right) => left.X > right.X || left.Y > right.Y || left.Z > right.Z;
        public static Vector3 GreaterThanOrEqual(Vector3 left, Vector3 right) => new(Mask(left.X >= right.X), Mask(left.Y >= right.Y), Mask(left.Z >= right.Z));
        public static bool GreaterThanOrEqualAll(Vector3 left, Vector3 right) => left.X >= right.X && left.Y >= right.Y && left.Z >= right.Z;
        public static bool GreaterThanOrEqualAny(Vector3 left, Vector3 right) => left.X >= right.X || left.Y >= right.Y || left.Z >= right.Z;
        public static Vector3 Hypot(Vector3 x, Vector3 y) => new(float.Hypot(x.X, y.X), float.Hypot(x.Y, y.Y), float.Hypot(x.Z, y.Z));
        public static int IndexOf(Vector3 vector, float value) => vector.X == value ? 0 : vector.Y == value ? 1 : vector.Z == value ? 2 : -1;
        public static int IndexOfWhereAllBitsSet(Vector3 vector) => Bits(vector.X) == -1 ? 0 : Bits(vector.Y) == -1 ? 1 : Bits(vector.Z) == -1 ? 2 : -1;
        public static Vector3 IsEvenInteger(Vector3 vector) => new(Mask(float.IsEvenInteger(vector.X)), Mask(float.IsEvenInteger(vector.Y)), Mask(float.IsEvenInteger(vector.Z)));
        public static Vector3 IsFinite(Vector3 vector) => new(Mask(float.IsFinite(vector.X)), Mask(float.IsFinite(vector.Y)), Mask(float.IsFinite(vector.Z)));
        public static Vector3 IsInfinity(Vector3 vector) => new(Mask(float.IsInfinity(vector.X)), Mask(float.IsInfinity(vector.Y)), Mask(float.IsInfinity(vector.Z)));
        public static Vector3 IsInteger(Vector3 vector) => new(Mask(float.IsInteger(vector.X)), Mask(float.IsInteger(vector.Y)), Mask(float.IsInteger(vector.Z)));
        public static Vector3 IsNaN(Vector3 vector) => new(Mask(float.IsNaN(vector.X)), Mask(float.IsNaN(vector.Y)), Mask(float.IsNaN(vector.Z)));
        public static Vector3 IsNegative(Vector3 vector) => new(Mask(float.IsNegative(vector.X)), Mask(float.IsNegative(vector.Y)), Mask(float.IsNegative(vector.Z)));
        public static Vector3 IsNegativeInfinity(Vector3 vector) => new(Mask(float.IsNegativeInfinity(vector.X)), Mask(float.IsNegativeInfinity(vector.Y)), Mask(float.IsNegativeInfinity(vector.Z)));
        public static Vector3 IsNormal(Vector3 vector) => new(Mask(float.IsNormal(vector.X)), Mask(float.IsNormal(vector.Y)), Mask(float.IsNormal(vector.Z)));
        public static Vector3 IsOddInteger(Vector3 vector) => new(Mask(float.IsOddInteger(vector.X)), Mask(float.IsOddInteger(vector.Y)), Mask(float.IsOddInteger(vector.Z)));
        public static Vector3 IsPositive(Vector3 vector) => new(Mask(float.IsPositive(vector.X)), Mask(float.IsPositive(vector.Y)), Mask(float.IsPositive(vector.Z)));
        public static Vector3 IsPositiveInfinity(Vector3 vector) => new(Mask(float.IsPositiveInfinity(vector.X)), Mask(float.IsPositiveInfinity(vector.Y)), Mask(float.IsPositiveInfinity(vector.Z)));
        public static Vector3 IsSubnormal(Vector3 vector) => new(Mask(float.IsSubnormal(vector.X)), Mask(float.IsSubnormal(vector.Y)), Mask(float.IsSubnormal(vector.Z)));
        public static Vector3 IsZero(Vector3 vector) => new(Mask(float.IsZero(vector.X)), Mask(float.IsZero(vector.Y)), Mask(float.IsZero(vector.Z)));
        public static int LastIndexOf(Vector3 vector, float value) => vector.Z == value ? 2 : vector.Y == value ? 1 : vector.X == value ? 0 : -1;
        public static int LastIndexOfWhereAllBitsSet(Vector3 vector) => Bits(vector.Z) == -1 ? 2 : Bits(vector.Y) == -1 ? 1 : Bits(vector.X) == -1 ? 0 : -1;
        public static Vector3 Lerp(Vector3 value1, Vector3 value2, float amount) => value1 + (value2 - value1) * amount;
        public static Vector3 Lerp(Vector3 value1, Vector3 value2, Vector3 amount) => value1 + (value2 - value1) * amount;
        public static Vector3 LessThan(Vector3 left, Vector3 right) => new(Mask(left.X < right.X), Mask(left.Y < right.Y), Mask(left.Z < right.Z));
        public static bool LessThanAll(Vector3 left, Vector3 right) => left.X < right.X && left.Y < right.Y && left.Z < right.Z;
        public static bool LessThanAny(Vector3 left, Vector3 right) => left.X < right.X || left.Y < right.Y || left.Z < right.Z;
        public static Vector3 LessThanOrEqual(Vector3 left, Vector3 right) => new(Mask(left.X <= right.X), Mask(left.Y <= right.Y), Mask(left.Z <= right.Z));
        public static bool LessThanOrEqualAll(Vector3 left, Vector3 right) => left.X <= right.X && left.Y <= right.Y && left.Z <= right.Z;
        public static bool LessThanOrEqualAny(Vector3 left, Vector3 right) => left.X <= right.X || left.Y <= right.Y || left.Z <= right.Z;
        public static unsafe Vector3 Load(float* source) => new(source[0], source[1], source[2]);
        public static unsafe Vector3 LoadAligned(float* source)
        {
            if ((nuint)source % Alignment != 0) throw new AccessViolationException();
            return Load(source);
        }
        public static unsafe Vector3 LoadAlignedNonTemporal(float* source) => LoadAligned(source);
        public static Vector3 LoadUnsafe(ref readonly float source) => new(source, Unsafe.Add(ref Unsafe.AsRef(in source), 1), Unsafe.Add(ref Unsafe.AsRef(in source), 2));
        public static Vector3 LoadUnsafe(ref readonly float source, nuint elementOffset)
        {
            var offset = (nint)elementOffset;
            return new(Unsafe.Add(ref Unsafe.AsRef(in source), offset), Unsafe.Add(ref Unsafe.AsRef(in source), offset + 1), Unsafe.Add(ref Unsafe.AsRef(in source), offset + 2));
        }
        public static Vector3 Log(Vector3 vector) => new(float.Log(vector.X), float.Log(vector.Y), float.Log(vector.Z));
        public static Vector3 Log2(Vector3 vector) => new(float.Log2(vector.X), float.Log2(vector.Y), float.Log2(vector.Z));
        public static Vector3 Max(Vector3 value1, Vector3 value2) => new(float.Max(value1.X, value2.X), float.Max(value1.Y, value2.Y), float.Max(value1.Z, value2.Z));
        public static Vector3 MaxMagnitude(Vector3 value1, Vector3 value2) => new(float.MaxMagnitude(value1.X, value2.X), float.MaxMagnitude(value1.Y, value2.Y), float.MaxMagnitude(value1.Z, value2.Z));
        public static Vector3 MaxMagnitudeNumber(Vector3 value1, Vector3 value2) => new(float.MaxMagnitudeNumber(value1.X, value2.X), float.MaxMagnitudeNumber(value1.Y, value2.Y), float.MaxMagnitudeNumber(value1.Z, value2.Z));
        public static Vector3 MaxNative(Vector3 value1, Vector3 value2) => Max(value1, value2);
        public static Vector3 MaxNumber(Vector3 value1, Vector3 value2) => Max(value1, value2);
        public static Vector3 Min(Vector3 value1, Vector3 value2) => new(float.Min(value1.X, value2.X), float.Min(value1.Y, value2.Y), float.Min(value1.Z, value2.Z));
        public static Vector3 MinMagnitude(Vector3 value1, Vector3 value2) => new(float.MinMagnitude(value1.X, value2.X), float.MinMagnitude(value1.Y, value2.Y), float.MinMagnitude(value1.Z, value2.Z));
        public static Vector3 MinMagnitudeNumber(Vector3 value1, Vector3 value2) => new(float.MinMagnitudeNumber(value1.X, value2.X), float.MinMagnitudeNumber(value1.Y, value2.Y), float.MinMagnitudeNumber(value1.Z, value2.Z));
        public static Vector3 MinNative(Vector3 value1, Vector3 value2) => Min(value1, value2);
        public static Vector3 MinNumber(Vector3 value1, Vector3 value2) => Min(value1, value2);
        public static Vector3 Multiply(Vector3 left, Vector3 right) => left * right;
        public static Vector3 Multiply(Vector3 left, float right) => left * right;
        public static Vector3 Multiply(float left, Vector3 right) => left * right;
        public static Vector3 MultiplyAddEstimate(Vector3 left, Vector3 right, Vector3 addend) => left * right + addend;
        public static Vector3 Negate(Vector3 value) => -value;
        public static bool None(Vector3 vector, float value) => vector.X != value && vector.Y != value && vector.Z != value;
        public static bool NoneWhereAllBitsSet(Vector3 vector) => Bits(vector.X) != -1 && Bits(vector.Y) != -1 && Bits(vector.Z) != -1;
        public static Vector3 Normalize(Vector3 value) => value / value.Length();
        public static Vector3 OnesComplement(Vector3 value) => new(FromBits(~Bits(value.X)), FromBits(~Bits(value.Y)), FromBits(~Bits(value.Z)));
        public static Vector3 RadiansToDegrees(Vector3 radians) => radians * (180f / float.Pi);
        public static Vector3 Reflect(Vector3 vector, Vector3 normal) => vector - (2f * Dot(vector, normal)) * normal;
        public static Vector3 Round(Vector3 vector) => new((float)Math.Round(vector.X), (float)Math.Round(vector.Y), (float)Math.Round(vector.Z));
        public static Vector3 Round(Vector3 vector, MidpointRounding mode) => new((float)Math.Round(vector.X, mode), (float)Math.Round(vector.Y, mode), (float)Math.Round(vector.Z, mode));
        public static Vector3 Shuffle(Vector3 vector, byte xIndex, byte yIndex, byte zIndex) => new(xIndex < ElementCount ? vector[xIndex] : 0f, yIndex < ElementCount ? vector[yIndex] : 0f, zIndex < ElementCount ? vector[zIndex] : 0f);
        public static Vector3 Sin(Vector3 vector) => new(float.Sin(vector.X), float.Sin(vector.Y), float.Sin(vector.Z));
        public static (Vector3 Sin, Vector3 Cos) SinCos(Vector3 vector) => (Sin(vector), Cos(vector));
        public static Vector3 SquareRoot(Vector3 value) => new(float.Sqrt(value.X), float.Sqrt(value.Y), float.Sqrt(value.Z));
        public static Vector3 Subtract(Vector3 left, Vector3 right) => left - right;
        public static float Sum(Vector3 value) => value.X + value.Y + value.Z;

        public static Vector3 Transform(Vector3 position, Matrix4x4 matrix) => new(
            position.X * matrix.M11 + position.Y * matrix.M21 + position.Z * matrix.M31 + matrix.M41,
            position.X * matrix.M12 + position.Y * matrix.M22 + position.Z * matrix.M32 + matrix.M42,
            position.X * matrix.M13 + position.Y * matrix.M23 + position.Z * matrix.M33 + matrix.M43);

        public static Vector3 TransformNormal(Vector3 normal, Matrix4x4 matrix) => new(
            normal.X * matrix.M11 + normal.Y * matrix.M21 + normal.Z * matrix.M31,
            normal.X * matrix.M12 + normal.Y * matrix.M22 + normal.Z * matrix.M32,
            normal.X * matrix.M13 + normal.Y * matrix.M23 + normal.Z * matrix.M33);

        /// <summary>Transforms a vector by the specified Quaternion rotation value.</summary>
        public static Vector3 Transform(Vector3 value, Quaternion rotation)
        {
            var transformed = Vector4.Transform(new Vector4(value, 1f), rotation);
            return new(transformed.X, transformed.Y, transformed.Z);
        }

        public static Vector3 Truncate(Vector3 vector) => new(float.Truncate(vector.X), float.Truncate(vector.Y), float.Truncate(vector.Z));
        public static Vector3 Xor(Vector3 left, Vector3 right) => new(XorScalar(left.X, right.X), XorScalar(left.Y, right.Y), XorScalar(left.Z, right.Z));

        public readonly void CopyTo(float[] array)
        {
            if (array.Length < ElementCount) throw new ArgumentException();
            array[0] = X; array[1] = Y; array[2] = Z;
        }
        public readonly void CopyTo(float[] array, int index)
        {
            if ((uint)index >= (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < ElementCount) throw new ArgumentException();
            array[index] = X; array[index + 1] = Y; array[index + 2] = Z;
        }
        public readonly void CopyTo(Span<float> destination)
        {
            if (destination.Length < ElementCount) throw new ArgumentException();
            destination[0] = X; destination[1] = Y; destination[2] = Z;
        }
        public readonly bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < ElementCount) return false;
            destination[0] = X; destination[1] = Y; destination[2] = Z;
            return true;
        }
        public readonly bool Equals(Vector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override readonly bool Equals(object? obj) => obj is Vector3 other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => X * X + Y * Y + Z * Z;
        public override readonly string ToString() => ToString("G", null);
        public readonly string ToString(string? format) => ToString(format, null);
        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            var separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            var handler = new DefaultInterpolatedStringHandler(3 + (separator.Length * 2), 3, formatProvider);
            handler.AppendLiteral("<"); handler.AppendFormatted(X, format); handler.AppendLiteral(separator); handler.AppendLiteral(" "); handler.AppendFormatted(Y, format); handler.AppendLiteral(separator); handler.AppendLiteral(" "); handler.AppendFormatted(Z, format); handler.AppendLiteral(">");
            return handler.ToStringAndClear();
        }
    }
}
