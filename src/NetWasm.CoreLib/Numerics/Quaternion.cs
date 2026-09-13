// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Scalar adaptation of dotnet/runtime System.Numerics.Quaternion.cs.

namespace System.Numerics;

public struct Quaternion : IEquatable<Quaternion>
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Quaternion(Vector3 vectorPart, float scalarPart)
    {
        X = vectorPart.X;
        Y = vectorPart.Y;
        Z = vectorPart.Z;
        W = scalarPart;
    }

    public static Quaternion Zero
    {
        get => default;
    }

    public static Quaternion Identity
    {
        get => new(0, 0, 0, 1);
    }

    public float this[int index]
    {
        readonly get => index switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            3 => W,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
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

    public readonly bool IsIdentity
    {
        get => X == 0 && Y == 0 && Z == 0 && W == 1;
    }

    public static Quaternion operator +(Quaternion left, Quaternion right) => new(
        left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    public static Quaternion operator /(Quaternion left, Quaternion right) =>
        Concatenate(Inverse(right), left);

    public static bool operator ==(Quaternion left, Quaternion right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    public static bool operator !=(Quaternion left, Quaternion right) => !(left == right);

    public static Quaternion operator *(Quaternion left, Quaternion right) =>
        Concatenate(right, left);

    public static Quaternion operator *(Quaternion value, float scale) => new(
        value.X * scale, value.Y * scale, value.Z * scale, value.W * scale);

    public static Quaternion operator -(Quaternion left, Quaternion right) => new(
        left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    public static Quaternion operator -(Quaternion value) => new(-value.X, -value.Y, -value.Z, -value.W);

    public static Quaternion Add(Quaternion value1, Quaternion value2) => value1 + value2;

    public static Quaternion Concatenate(Quaternion value1, Quaternion value2) => new(
        value1.X * value2.W + value1.W * value2.X + value1.Z * value2.Y - value1.Y * value2.Z,
        value1.Y * value2.W - value1.Z * value2.X + value1.W * value2.Y + value1.X * value2.Z,
        value1.Z * value2.W + value1.Y * value2.X - value1.X * value2.Y + value1.W * value2.Z,
        value1.W * value2.W - value1.X * value2.X - value1.Y * value2.Y - value1.Z * value2.Z);

    public static Quaternion Conjugate(Quaternion value) =>
        new(-value.X, -value.Y, -value.Z, value.W);

    public static Quaternion Create(float x, float y, float z, float w) => new(x, y, z, w);
    public static Quaternion Create(Vector3 vectorPart, float scalarPart) => new(vectorPart, scalarPart);

    public static Quaternion CreateFromAxisAngle(Vector3 axis, float angle)
    {
        (float s, float c) = MathF.SinCos(angle * 0.5f);
        return new(axis.X * s, axis.Y * s, axis.Z * s, c);
    }

    public static Quaternion CreateFromYawPitchRoll(float yaw, float pitch, float roll)
    {
        (float sr, float cr) = MathF.SinCos(roll * 0.5f);
        (float sp, float cp) = MathF.SinCos(pitch * 0.5f);
        (float sy, float cy) = MathF.SinCos(yaw * 0.5f);

        return new(
            cy * sp * cr + sy * cp * sr,
            sy * cp * cr - cy * sp * sr,
            cy * cp * sr - sy * sp * cr,
            cy * cp * cr + sy * sp * sr);
    }

    public static Quaternion CreateFromRotationMatrix(Matrix4x4 matrix)
    {
        var trace = matrix.M11 + matrix.M22 + matrix.M33;
        if (trace > 0f)
        {
            var s = MathF.Sqrt(trace + 1f);
            var inverseS = 0.5f / s;
            return new(
                (matrix.M23 - matrix.M32) * inverseS,
                (matrix.M31 - matrix.M13) * inverseS,
                (matrix.M12 - matrix.M21) * inverseS,
                s * 0.5f);
        }

        if (matrix.M11 >= matrix.M22 && matrix.M11 >= matrix.M33)
        {
            var s = MathF.Sqrt(1f + matrix.M11 - matrix.M22 - matrix.M33);
            var inverseS = 0.5f / s;
            return new(
                0.5f * s,
                (matrix.M12 + matrix.M21) * inverseS,
                (matrix.M13 + matrix.M31) * inverseS,
                (matrix.M23 - matrix.M32) * inverseS);
        }

        if (matrix.M22 > matrix.M33)
        {
            var s = MathF.Sqrt(1f + matrix.M22 - matrix.M11 - matrix.M33);
            var inverseS = 0.5f / s;
            return new(
                (matrix.M21 + matrix.M12) * inverseS,
                0.5f * s,
                (matrix.M32 + matrix.M23) * inverseS,
                (matrix.M31 - matrix.M13) * inverseS);
        }

        {
            var s = MathF.Sqrt(1f + matrix.M33 - matrix.M11 - matrix.M22);
            var inverseS = 0.5f / s;
            return new(
                (matrix.M31 + matrix.M13) * inverseS,
                (matrix.M32 + matrix.M23) * inverseS,
                0.5f * s,
                (matrix.M12 - matrix.M21) * inverseS);
        }
    }

    public static Quaternion Divide(Quaternion value1, Quaternion value2) => value1 / value2;

    public static float Dot(Quaternion quaternion1, Quaternion quaternion2) =>
        quaternion1.X * quaternion2.X +
        quaternion1.Y * quaternion2.Y +
        quaternion1.Z * quaternion2.Z +
        quaternion1.W * quaternion2.W;

    public static Quaternion Inverse(Quaternion value)
    {
        const float Epsilon = 1.192092896e-7f;
        float lengthSquared = value.LengthSquared();
        if (lengthSquared <= Epsilon)
        {
            return default;
        }

        return Conjugate(value) * (1 / lengthSquared);
    }

    public static Quaternion Lerp(Quaternion quaternion1, Quaternion quaternion2, float amount)
    {
        Quaternion second = IsNegative(Dot(quaternion1, quaternion2)) ? -quaternion2 : quaternion2;
        return Normalize(quaternion1 * (1 - amount) + second * amount);
    }

    public static Quaternion Multiply(Quaternion value1, Quaternion value2) => value1 * value2;
    public static Quaternion Multiply(Quaternion value1, float value2) => value1 * value2;
    public static Quaternion Negate(Quaternion value) => -value;

    public static Quaternion Normalize(Quaternion value)
    {
        float length = value.Length();
        return value * (1 / length);
    }

    public static Quaternion Slerp(Quaternion quaternion1, Quaternion quaternion2, float amount)
    {
        const float SlerpEpsilon = 1e-6f;
        float cosOmega = Dot(quaternion1, quaternion2);
        float sign = 1;

        if (IsNegative(cosOmega))
        {
            cosOmega = -cosOmega;
            sign = -1;
        }

        float s1;
        float s2;
        if (cosOmega > 1 - SlerpEpsilon)
        {
            s1 = 1 - amount;
            s2 = amount * sign;
        }
        else
        {
            float omega = MathF.Acos(cosOmega);
            float inverseSinOmega = 1 / MathF.Sin(omega);
            s1 = MathF.Sin((1 - amount) * omega) * inverseSinOmega;
            s2 = MathF.Sin(amount * omega) * inverseSinOmega * sign;
        }

        return quaternion1 * s1 + quaternion2 * s2;
    }

    public static Quaternion Subtract(Quaternion value1, Quaternion value2) => value1 - value2;

    public override readonly bool Equals(object? obj) => obj is Quaternion other && Equals(other);

    public readonly bool Equals(Quaternion other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public readonly float Length() => MathF.Sqrt(LengthSquared());

    public readonly float LengthSquared() => X * X + Y * Y + Z * Z + W * W;

    public override readonly string ToString() => $"{{X:{X} Y:{Y} Z:{Z} W:{W}}}";

    private static bool IsNegative(float value) =>
        value < 0 || value == 0 && 1 / value == float.NegativeInfinity;
}
