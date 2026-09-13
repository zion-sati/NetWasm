// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Numerics
{
    /// <summary>Represents a vector with four single-precision floating-point values.</summary>
    public partial struct Vector4 : IEquatable<Vector4>, IFormattable
    {
        private const int ElementCount = 4;
        private const int Alignment = 16;

        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vector4(Vector2 value, float z, float w) { X = value.X; Y = value.Y; Z = z; W = w; }
        public Vector4(Vector3 value, float w) { X = value.X; Y = value.Y; Z = value.Z; W = w; }
        public Vector4(float value) { X = value; Y = value; Z = value; W = value; }
        public Vector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public Vector4(ReadOnlySpan<float> values)
        {
            if (values.Length < ElementCount) throw new ArgumentOutOfRangeException(nameof(values));
            X = values[0]; Y = values[1]; Z = values[2]; W = values[3];
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

        public static Vector4 AllBitsSet
        {
            get => new(FromBits(-1));
        }

        public static Vector4 E
        {
            get => new(float.E);
        }

        public static Vector4 Epsilon
        {
            get => new(float.Epsilon);
        }

        public static Vector4 NaN
        {
            get => new(float.NaN);
        }

        public static Vector4 NegativeInfinity
        {
            get => new(float.NegativeInfinity);
        }

        public static Vector4 NegativeZero
        {
            get => new(float.NegativeZero);
        }

        public static Vector4 One
        {
            get => new(1f);
        }

        public static Vector4 Pi
        {
            get => new(float.Pi);
        }

        public static Vector4 PositiveInfinity
        {
            get => new(float.PositiveInfinity);
        }

        public static Vector4 Tau
        {
            get => new(float.Tau);
        }

        public static Vector4 UnitW
        {
            get => new(0f, 0f, 0f, 1f);
        }

        public static Vector4 UnitX
        {
            get => new(1f, 0f, 0f, 0f);
        }

        public static Vector4 UnitY
        {
            get => new(0f, 1f, 0f, 0f);
        }

        public static Vector4 UnitZ
        {
            get => new(0f, 0f, 1f, 0f);
        }

        public static Vector4 Zero
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
                3 => W,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            set
            {
                switch (index)
                {
                    case 0: X = value; break;
                    case 1: Y = value; break;
                    case 2: Z = value; break;
                    case 3: W = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public static Vector4 operator +(Vector4 left, Vector4 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        public static Vector4 operator -(Vector4 left, Vector4 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
        public static Vector4 operator -(Vector4 value) => new(-value.X, -value.Y, -value.Z, -value.W);
        public static Vector4 operator +(Vector4 value) => value;
        public static Vector4 operator *(Vector4 left, Vector4 right) => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);
        public static Vector4 operator *(Vector4 left, float right) => new(left.X * right, left.Y * right, left.Z * right, left.W * right);
        public static Vector4 operator *(float left, Vector4 right) => right * left;
        public static Vector4 operator /(Vector4 left, Vector4 right) => new(left.X / right.X, left.Y / right.Y, left.Z / right.Z, left.W / right.W);
        public static Vector4 operator /(Vector4 left, float right) => new(left.X / right, left.Y / right, left.Z / right, left.W / right);
        public static bool operator ==(Vector4 left, Vector4 right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
        public static bool operator !=(Vector4 left, Vector4 right) => !(left == right);
        public static Vector4 operator &(Vector4 left, Vector4 right) => BitwiseAnd(left, right);
        public static Vector4 operator |(Vector4 left, Vector4 right) => BitwiseOr(left, right);
        public static Vector4 operator ^(Vector4 left, Vector4 right) => Xor(left, right);
        public static Vector4 operator ~(Vector4 value) => OnesComplement(value);
        public static Vector4 operator <<(Vector4 value, int shiftAmount) => new(ShiftLeft(value.X, shiftAmount), ShiftLeft(value.Y, shiftAmount), ShiftLeft(value.Z, shiftAmount), ShiftLeft(value.W, shiftAmount));
        public static Vector4 operator >>(Vector4 value, int shiftAmount) => new(ShiftRight(value.X, shiftAmount), ShiftRight(value.Y, shiftAmount), ShiftRight(value.Z, shiftAmount), ShiftRight(value.W, shiftAmount));
        public static Vector4 operator >>>(Vector4 value, int shiftAmount) => new(ShiftRightLogical(value.X, shiftAmount), ShiftRightLogical(value.Y, shiftAmount), ShiftRightLogical(value.Z, shiftAmount), ShiftRightLogical(value.W, shiftAmount));

        public static Vector4 Abs(Vector4 value) => new(float.Abs(value.X), float.Abs(value.Y), float.Abs(value.Z), float.Abs(value.W));
        public static Vector4 Add(Vector4 left, Vector4 right) => left + right;
        public static bool All(Vector4 vector, float value) => vector.X == value && vector.Y == value && vector.Z == value && vector.W == value;
        public static bool AllWhereAllBitsSet(Vector4 vector) => Bits(vector.X) == -1 && Bits(vector.Y) == -1 && Bits(vector.Z) == -1 && Bits(vector.W) == -1;
        public static Vector4 AndNot(Vector4 left, Vector4 right) => new(AndNotScalar(left.X, right.X), AndNotScalar(left.Y, right.Y), AndNotScalar(left.Z, right.Z), AndNotScalar(left.W, right.W));
        public static bool Any(Vector4 vector, float value) => vector.X == value || vector.Y == value || vector.Z == value || vector.W == value;
        public static bool AnyWhereAllBitsSet(Vector4 vector) => Bits(vector.X) == -1 || Bits(vector.Y) == -1 || Bits(vector.Z) == -1 || Bits(vector.W) == -1;
        public static Vector4 BitwiseAnd(Vector4 left, Vector4 right) => new(And(left.X, right.X), And(left.Y, right.Y), And(left.Z, right.Z), And(left.W, right.W));
        public static Vector4 BitwiseOr(Vector4 left, Vector4 right) => new(Or(left.X, right.X), Or(left.Y, right.Y), Or(left.Z, right.Z), Or(left.W, right.W));
        public static Vector4 Clamp(Vector4 value1, Vector4 min, Vector4 max) => new(float.Clamp(value1.X, min.X, max.X), float.Clamp(value1.Y, min.Y, max.Y), float.Clamp(value1.Z, min.Z, max.Z), float.Clamp(value1.W, min.W, max.W));
        public static Vector4 ClampNative(Vector4 value1, Vector4 min, Vector4 max) => new(float.Max(float.Min(value1.X, max.X), min.X), float.Max(float.Min(value1.Y, max.Y), min.Y), float.Max(float.Min(value1.Z, max.Z), min.Z), float.Max(float.Min(value1.W, max.W), min.W));
        public static Vector4 ConditionalSelect(Vector4 condition, Vector4 left, Vector4 right) => new(Select(condition.X, left.X, right.X), Select(condition.Y, left.Y, right.Y), Select(condition.Z, left.Z, right.Z), Select(condition.W, left.W, right.W));
        public static Vector4 CopySign(Vector4 value, Vector4 sign) => new(float.CopySign(value.X, sign.X), float.CopySign(value.Y, sign.Y), float.CopySign(value.Z, sign.Z), float.CopySign(value.W, sign.W));
        public static Vector4 Cos(Vector4 vector) => new(float.Cos(vector.X), float.Cos(vector.Y), float.Cos(vector.Z), float.Cos(vector.W));
        public static int Count(Vector4 vector, float value) => (vector.X == value ? 1 : 0) + (vector.Y == value ? 1 : 0) + (vector.Z == value ? 1 : 0) + (vector.W == value ? 1 : 0);
        public static int CountWhereAllBitsSet(Vector4 vector) => (Bits(vector.X) == -1 ? 1 : 0) + (Bits(vector.Y) == -1 ? 1 : 0) + (Bits(vector.Z) == -1 ? 1 : 0) + (Bits(vector.W) == -1 ? 1 : 0);
        public static Vector4 Create(float value) => new(value);
        public static Vector4 Create(Vector2 vector, float z, float w) => new(vector, z, w);
        public static Vector4 Create(Vector3 vector, float w) => new(vector, w);
        public static Vector4 Create(float x, float y, float z, float w) => new(x, y, z, w);
        public static Vector4 Create(ReadOnlySpan<float> values) => new(values);
        public static Vector4 CreateScalar(float x) => new(x, 0f, 0f, 0f);
        public static unsafe Vector4 CreateScalarUnsafe(float x) => new(x, 0f, 0f, 0f);
        public static Vector4 DegreesToRadians(Vector4 degrees) => degrees * (float.Pi / 180f);
        public static float Distance(Vector4 value1, Vector4 value2) => (value1 - value2).Length();
        public static float DistanceSquared(Vector4 value1, Vector4 value2) => (value1 - value2).LengthSquared();
        public static Vector4 Divide(Vector4 left, Vector4 right) => left / right;
        public static Vector4 Divide(Vector4 left, float divisor) => left / divisor;
        public static float Dot(Vector4 vector1, Vector4 vector2) => vector1.X * vector2.X + vector1.Y * vector2.Y + vector1.Z * vector2.Z + vector1.W * vector2.W;
        public static Vector4 Cross(Vector4 vector1, Vector4 vector2) => new(vector1.Y * vector2.Z - vector1.Z * vector2.Y, vector1.Z * vector2.X - vector1.X * vector2.Z, vector1.X * vector2.Y - vector1.Y * vector2.X, vector1.W * vector2.W);
        public static Vector4 Equals(Vector4 left, Vector4 right) => new(Mask(left.X == right.X), Mask(left.Y == right.Y), Mask(left.Z == right.Z), Mask(left.W == right.W));
        public static bool EqualsAll(Vector4 left, Vector4 right) => left == right;
        public static bool EqualsAny(Vector4 left, Vector4 right) => left.X == right.X || left.Y == right.Y || left.Z == right.Z || left.W == right.W;
        public static Vector4 Exp(Vector4 vector) => new(float.Exp(vector.X), float.Exp(vector.Y), float.Exp(vector.Z), float.Exp(vector.W));
        public static Vector4 FusedMultiplyAdd(Vector4 left, Vector4 right, Vector4 addend) => new(float.FusedMultiplyAdd(left.X, right.X, addend.X), float.FusedMultiplyAdd(left.Y, right.Y, addend.Y), float.FusedMultiplyAdd(left.Z, right.Z, addend.Z), float.FusedMultiplyAdd(left.W, right.W, addend.W));
        public static Vector4 GreaterThan(Vector4 left, Vector4 right) => new(Mask(left.X > right.X), Mask(left.Y > right.Y), Mask(left.Z > right.Z), Mask(left.W > right.W));
        public static bool GreaterThanAll(Vector4 left, Vector4 right) => left.X > right.X && left.Y > right.Y && left.Z > right.Z && left.W > right.W;
        public static bool GreaterThanAny(Vector4 left, Vector4 right) => left.X > right.X || left.Y > right.Y || left.Z > right.Z || left.W > right.W;
        public static Vector4 GreaterThanOrEqual(Vector4 left, Vector4 right) => new(Mask(left.X >= right.X), Mask(left.Y >= right.Y), Mask(left.Z >= right.Z), Mask(left.W >= right.W));
        public static bool GreaterThanOrEqualAll(Vector4 left, Vector4 right) => left.X >= right.X && left.Y >= right.Y && left.Z >= right.Z && left.W >= right.W;
        public static bool GreaterThanOrEqualAny(Vector4 left, Vector4 right) => left.X >= right.X || left.Y >= right.Y || left.Z >= right.Z || left.W >= right.W;
        public static Vector4 Hypot(Vector4 x, Vector4 y) => new(float.Hypot(x.X, y.X), float.Hypot(x.Y, y.Y), float.Hypot(x.Z, y.Z), float.Hypot(x.W, y.W));
        public static int IndexOf(Vector4 vector, float value) => vector.X == value ? 0 : vector.Y == value ? 1 : vector.Z == value ? 2 : vector.W == value ? 3 : -1;
        public static int IndexOfWhereAllBitsSet(Vector4 vector) => Bits(vector.X) == -1 ? 0 : Bits(vector.Y) == -1 ? 1 : Bits(vector.Z) == -1 ? 2 : Bits(vector.W) == -1 ? 3 : -1;
        public static Vector4 IsEvenInteger(Vector4 vector) => new(Mask(float.IsEvenInteger(vector.X)), Mask(float.IsEvenInteger(vector.Y)), Mask(float.IsEvenInteger(vector.Z)), Mask(float.IsEvenInteger(vector.W)));
        public static Vector4 IsFinite(Vector4 vector) => new(Mask(float.IsFinite(vector.X)), Mask(float.IsFinite(vector.Y)), Mask(float.IsFinite(vector.Z)), Mask(float.IsFinite(vector.W)));
        public static Vector4 IsInfinity(Vector4 vector) => new(Mask(float.IsInfinity(vector.X)), Mask(float.IsInfinity(vector.Y)), Mask(float.IsInfinity(vector.Z)), Mask(float.IsInfinity(vector.W)));
        public static Vector4 IsInteger(Vector4 vector) => new(Mask(float.IsInteger(vector.X)), Mask(float.IsInteger(vector.Y)), Mask(float.IsInteger(vector.Z)), Mask(float.IsInteger(vector.W)));
        public static Vector4 IsNaN(Vector4 vector) => new(Mask(float.IsNaN(vector.X)), Mask(float.IsNaN(vector.Y)), Mask(float.IsNaN(vector.Z)), Mask(float.IsNaN(vector.W)));
        public static Vector4 IsNegative(Vector4 vector) => new(Mask(float.IsNegative(vector.X)), Mask(float.IsNegative(vector.Y)), Mask(float.IsNegative(vector.Z)), Mask(float.IsNegative(vector.W)));
        public static Vector4 IsNegativeInfinity(Vector4 vector) => new(Mask(float.IsNegativeInfinity(vector.X)), Mask(float.IsNegativeInfinity(vector.Y)), Mask(float.IsNegativeInfinity(vector.Z)), Mask(float.IsNegativeInfinity(vector.W)));
        public static Vector4 IsNormal(Vector4 vector) => new(Mask(float.IsNormal(vector.X)), Mask(float.IsNormal(vector.Y)), Mask(float.IsNormal(vector.Z)), Mask(float.IsNormal(vector.W)));
        public static Vector4 IsOddInteger(Vector4 vector) => new(Mask(float.IsOddInteger(vector.X)), Mask(float.IsOddInteger(vector.Y)), Mask(float.IsOddInteger(vector.Z)), Mask(float.IsOddInteger(vector.W)));
        public static Vector4 IsPositive(Vector4 vector) => new(Mask(float.IsPositive(vector.X)), Mask(float.IsPositive(vector.Y)), Mask(float.IsPositive(vector.Z)), Mask(float.IsPositive(vector.W)));
        public static Vector4 IsPositiveInfinity(Vector4 vector) => new(Mask(float.IsPositiveInfinity(vector.X)), Mask(float.IsPositiveInfinity(vector.Y)), Mask(float.IsPositiveInfinity(vector.Z)), Mask(float.IsPositiveInfinity(vector.W)));
        public static Vector4 IsSubnormal(Vector4 vector) => new(Mask(float.IsSubnormal(vector.X)), Mask(float.IsSubnormal(vector.Y)), Mask(float.IsSubnormal(vector.Z)), Mask(float.IsSubnormal(vector.W)));
        public static Vector4 IsZero(Vector4 vector) => new(Mask(float.IsZero(vector.X)), Mask(float.IsZero(vector.Y)), Mask(float.IsZero(vector.Z)), Mask(float.IsZero(vector.W)));
        public static int LastIndexOf(Vector4 vector, float value) => vector.W == value ? 3 : vector.Z == value ? 2 : vector.Y == value ? 1 : vector.X == value ? 0 : -1;
        public static int LastIndexOfWhereAllBitsSet(Vector4 vector) => Bits(vector.W) == -1 ? 3 : Bits(vector.Z) == -1 ? 2 : Bits(vector.Y) == -1 ? 1 : Bits(vector.X) == -1 ? 0 : -1;
        public static Vector4 Lerp(Vector4 value1, Vector4 value2, float amount) => value1 + (value2 - value1) * amount;
        public static Vector4 Lerp(Vector4 value1, Vector4 value2, Vector4 amount) => value1 + (value2 - value1) * amount;
        public static Vector4 LessThan(Vector4 left, Vector4 right) => new(Mask(left.X < right.X), Mask(left.Y < right.Y), Mask(left.Z < right.Z), Mask(left.W < right.W));
        public static bool LessThanAll(Vector4 left, Vector4 right) => left.X < right.X && left.Y < right.Y && left.Z < right.Z && left.W < right.W;
        public static bool LessThanAny(Vector4 left, Vector4 right) => left.X < right.X || left.Y < right.Y || left.Z < right.Z || left.W < right.W;
        public static Vector4 LessThanOrEqual(Vector4 left, Vector4 right) => new(Mask(left.X <= right.X), Mask(left.Y <= right.Y), Mask(left.Z <= right.Z), Mask(left.W <= right.W));
        public static bool LessThanOrEqualAll(Vector4 left, Vector4 right) => left.X <= right.X && left.Y <= right.Y && left.Z <= right.Z && left.W <= right.W;
        public static bool LessThanOrEqualAny(Vector4 left, Vector4 right) => left.X <= right.X || left.Y <= right.Y || left.Z <= right.Z || left.W <= right.W;
        public static Vector4 Log(Vector4 vector) => new(float.Log(vector.X), float.Log(vector.Y), float.Log(vector.Z), float.Log(vector.W));
        public static Vector4 Log2(Vector4 vector) => new(float.Log2(vector.X), float.Log2(vector.Y), float.Log2(vector.Z), float.Log2(vector.W));
        public static unsafe Vector4 Load(float* source) => new(source[0], source[1], source[2], source[3]);
        public static unsafe Vector4 LoadAligned(float* source)
        {
            if ((nuint)source % Alignment != 0) throw new AccessViolationException();
            return Load(source);
        }
        public static unsafe Vector4 LoadAlignedNonTemporal(float* source) => LoadAligned(source);
        public static Vector4 LoadUnsafe(ref readonly float source) => new(source, Unsafe.Add(ref Unsafe.AsRef(in source), 1), Unsafe.Add(ref Unsafe.AsRef(in source), 2), Unsafe.Add(ref Unsafe.AsRef(in source), 3));
        public static Vector4 LoadUnsafe(ref readonly float source, nuint elementOffset)
        {
            var offset = (nint)elementOffset;
            return new(Unsafe.Add(ref Unsafe.AsRef(in source), offset), Unsafe.Add(ref Unsafe.AsRef(in source), offset + 1), Unsafe.Add(ref Unsafe.AsRef(in source), offset + 2), Unsafe.Add(ref Unsafe.AsRef(in source), offset + 3));
        }
        public static Vector4 Max(Vector4 value1, Vector4 value2) => new(float.Max(value1.X, value2.X), float.Max(value1.Y, value2.Y), float.Max(value1.Z, value2.Z), float.Max(value1.W, value2.W));
        public static Vector4 MaxMagnitude(Vector4 value1, Vector4 value2) => new(float.MaxMagnitude(value1.X, value2.X), float.MaxMagnitude(value1.Y, value2.Y), float.MaxMagnitude(value1.Z, value2.Z), float.MaxMagnitude(value1.W, value2.W));
        public static Vector4 MaxMagnitudeNumber(Vector4 value1, Vector4 value2) => new(float.MaxMagnitudeNumber(value1.X, value2.X), float.MaxMagnitudeNumber(value1.Y, value2.Y), float.MaxMagnitudeNumber(value1.Z, value2.Z), float.MaxMagnitudeNumber(value1.W, value2.W));
        public static Vector4 MaxNative(Vector4 value1, Vector4 value2) => Max(value1, value2);
        public static Vector4 MaxNumber(Vector4 value1, Vector4 value2) => Max(value1, value2);
        public static Vector4 Min(Vector4 value1, Vector4 value2) => new(float.Min(value1.X, value2.X), float.Min(value1.Y, value2.Y), float.Min(value1.Z, value2.Z), float.Min(value1.W, value2.W));
        public static Vector4 MinMagnitude(Vector4 value1, Vector4 value2) => new(float.MinMagnitude(value1.X, value2.X), float.MinMagnitude(value1.Y, value2.Y), float.MinMagnitude(value1.Z, value2.Z), float.MinMagnitude(value1.W, value2.W));
        public static Vector4 MinMagnitudeNumber(Vector4 value1, Vector4 value2) => new(float.MinMagnitudeNumber(value1.X, value2.X), float.MinMagnitudeNumber(value1.Y, value2.Y), float.MinMagnitudeNumber(value1.Z, value2.Z), float.MinMagnitudeNumber(value1.W, value2.W));
        public static Vector4 MinNative(Vector4 value1, Vector4 value2) => Min(value1, value2);
        public static Vector4 MinNumber(Vector4 value1, Vector4 value2) => Min(value1, value2);
        public static Vector4 Multiply(Vector4 left, Vector4 right) => left * right;
        public static Vector4 Multiply(Vector4 left, float right) => left * right;
        public static Vector4 Multiply(float left, Vector4 right) => left * right;
        public static Vector4 MultiplyAddEstimate(Vector4 left, Vector4 right, Vector4 addend) => left * right + addend;
        public static Vector4 Negate(Vector4 value) => -value;
        public static bool None(Vector4 vector, float value) => vector.X != value && vector.Y != value && vector.Z != value && vector.W != value;
        public static bool NoneWhereAllBitsSet(Vector4 vector) => Bits(vector.X) != -1 && Bits(vector.Y) != -1 && Bits(vector.Z) != -1 && Bits(vector.W) != -1;
        public static Vector4 Normalize(Vector4 vector) => vector / vector.Length();
        public static Vector4 OnesComplement(Vector4 value) => new(FromBits(~Bits(value.X)), FromBits(~Bits(value.Y)), FromBits(~Bits(value.Z)), FromBits(~Bits(value.W)));
        public static Vector4 RadiansToDegrees(Vector4 radians) => radians * (180f / float.Pi);
        public static Vector4 Round(Vector4 vector) => new((float)Math.Round(vector.X), (float)Math.Round(vector.Y), (float)Math.Round(vector.Z), (float)Math.Round(vector.W));
        public static Vector4 Round(Vector4 vector, MidpointRounding mode) => new((float)Math.Round(vector.X, mode), (float)Math.Round(vector.Y, mode), (float)Math.Round(vector.Z, mode), (float)Math.Round(vector.W, mode));
        public static Vector4 Shuffle(Vector4 vector, byte xIndex, byte yIndex, byte zIndex, byte wIndex) => new(xIndex < ElementCount ? vector[xIndex] : 0f, yIndex < ElementCount ? vector[yIndex] : 0f, zIndex < ElementCount ? vector[zIndex] : 0f, wIndex < ElementCount ? vector[wIndex] : 0f);
        public static Vector4 Sin(Vector4 vector) => new(float.Sin(vector.X), float.Sin(vector.Y), float.Sin(vector.Z), float.Sin(vector.W));
        public static (Vector4 Sin, Vector4 Cos) SinCos(Vector4 vector) => (Sin(vector), Cos(vector));
        public static Vector4 SquareRoot(Vector4 value) => new(float.Sqrt(value.X), float.Sqrt(value.Y), float.Sqrt(value.Z), float.Sqrt(value.W));
        public static Vector4 Subtract(Vector4 left, Vector4 right) => left - right;
        public static float Sum(Vector4 value) => value.X + value.Y + value.Z + value.W;

        public static Vector4 Transform(Vector2 position, Matrix4x4 matrix) => new(
            position.X * matrix.M11 + position.Y * matrix.M21 + matrix.M41,
            position.X * matrix.M12 + position.Y * matrix.M22 + matrix.M42,
            position.X * matrix.M13 + position.Y * matrix.M23 + matrix.M43,
            position.X * matrix.M14 + position.Y * matrix.M24 + matrix.M44);

        public static Vector4 Transform(Vector3 position, Matrix4x4 matrix) => new(
            position.X * matrix.M11 + position.Y * matrix.M21 + position.Z * matrix.M31 + matrix.M41,
            position.X * matrix.M12 + position.Y * matrix.M22 + position.Z * matrix.M32 + matrix.M42,
            position.X * matrix.M13 + position.Y * matrix.M23 + position.Z * matrix.M33 + matrix.M43,
            position.X * matrix.M14 + position.Y * matrix.M24 + position.Z * matrix.M34 + matrix.M44);

        public static Vector4 Transform(Vector4 vector, Matrix4x4 matrix) => new(
            vector.X * matrix.M11 + vector.Y * matrix.M21 + vector.Z * matrix.M31 + vector.W * matrix.M41,
            vector.X * matrix.M12 + vector.Y * matrix.M22 + vector.Z * matrix.M32 + vector.W * matrix.M42,
            vector.X * matrix.M13 + vector.Y * matrix.M23 + vector.Z * matrix.M33 + vector.W * matrix.M43,
            vector.X * matrix.M14 + vector.Y * matrix.M24 + vector.Z * matrix.M34 + vector.W * matrix.M44);

        /// <summary>Transforms a two-dimensional vector by the specified Quaternion rotation value.</summary>
        public static Vector4 Transform(Vector2 value, Quaternion rotation) => Transform(new Vector4(value, 0f, 1f), rotation);

        /// <summary>Transforms a three-dimensional vector by the specified Quaternion rotation value.</summary>
        public static Vector4 Transform(Vector3 value, Quaternion rotation) => Transform(new Vector4(value, 1f), rotation);

        /// <summary>Transforms a four-dimensional vector by the specified Quaternion rotation value.</summary>
        public static Vector4 Transform(Vector4 value, Quaternion rotation)
        {
            var vector = new Quaternion(value.X, value.Y, value.Z, value.W);
            var conjugate = Quaternion.Conjugate(rotation);
            var temp = Quaternion.Concatenate(conjugate, vector);
            var transformed = Quaternion.Concatenate(temp, rotation);
            return new(transformed.X, transformed.Y, transformed.Z, transformed.W);
        }

        public static Vector4 Truncate(Vector4 vector) => new(float.Truncate(vector.X), float.Truncate(vector.Y), float.Truncate(vector.Z), float.Truncate(vector.W));
        public static Vector4 Xor(Vector4 left, Vector4 right) => new(XorScalar(left.X, right.X), XorScalar(left.Y, right.Y), XorScalar(left.Z, right.Z), XorScalar(left.W, right.W));

        public readonly void CopyTo(float[] array)
        {
            if (array.Length < ElementCount) throw new ArgumentException();
            array[0] = X; array[1] = Y; array[2] = Z; array[3] = W;
        }
        public readonly void CopyTo(float[] array, int index)
        {
            if ((uint)index >= (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < ElementCount) throw new ArgumentException();
            array[index] = X; array[index + 1] = Y; array[index + 2] = Z; array[index + 3] = W;
        }
        public readonly void CopyTo(Span<float> destination)
        {
            if (destination.Length < ElementCount) throw new ArgumentException();
            destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W;
        }
        public readonly bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < ElementCount) return false;
            destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W;
            return true;
        }
        public readonly bool Equals(Vector4 other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override readonly bool Equals(object? obj) => obj is Vector4 other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public readonly float Length() => float.Sqrt(LengthSquared());
        public readonly float LengthSquared() => X * X + Y * Y + Z * Z + W * W;
        public override readonly string ToString() => ToString("G", null);
        public readonly string ToString(string? format) => ToString(format, null);
        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            var separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            var handler = new DefaultInterpolatedStringHandler(3 + (separator.Length * 3), 4, formatProvider);
            handler.AppendLiteral("<"); handler.AppendFormatted(X, format); handler.AppendLiteral(separator); handler.AppendLiteral(" "); handler.AppendFormatted(Y, format); handler.AppendLiteral(separator); handler.AppendLiteral(" "); handler.AppendFormatted(Z, format); handler.AppendLiteral(separator); handler.AppendLiteral(" "); handler.AppendFormatted(W, format); handler.AppendLiteral(">");
            return handler.ToStringAndClear();
        }
    }
}
