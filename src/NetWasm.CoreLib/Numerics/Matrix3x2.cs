// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Scalar adaptation of dotnet/runtime System.Numerics.Matrix3x2.cs.

namespace System.Numerics;

public struct Matrix3x2 : IEquatable<Matrix3x2>
{
    private const float InverseEpsilon = 2.938737E-39f;
    private const float RotationEpsilon = 1.7453292E-05f;

    public float M11;
    public float M12;
    public float M21;
    public float M22;
    public float M31;
    public float M32;

    public Matrix3x2(float m11, float m12, float m21, float m22, float m31, float m32)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        M31 = m31;
        M32 = m32;
    }

    public static Matrix3x2 Identity
    {
        get => new(1, 0, 0, 1, 0, 0);
    }

    public readonly bool IsIdentity
    {
        get =>
            M11 == 1 && M12 == 0 &&
            M21 == 0 && M22 == 1 &&
            M31 == 0 && M32 == 0;
    }

    public Vector2 Translation
    {
        readonly get => new(M31, M32);
        set
        {
            M31 = value.X;
            M32 = value.Y;
        }
    }

    public Vector2 X
    {
        readonly get => new(M11, M12);
        set
        {
            M11 = value.X;
            M12 = value.Y;
        }
    }

    public Vector2 Y
    {
        readonly get => new(M21, M22);
        set
        {
            M21 = value.X;
            M22 = value.Y;
        }
    }

    public Vector2 Z
    {
        readonly get => new(M31, M32);
        set
        {
            M31 = value.X;
            M32 = value.Y;
        }
    }

    public Vector2 this[int row]
    {
        readonly get => row switch
        {
            0 => new Vector2(M11, M12),
            1 => new Vector2(M21, M22),
            2 => new Vector2(M31, M32),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };
        set
        {
            switch (row)
            {
                case 0:
                    M11 = value.X;
                    M12 = value.Y;
                    break;
                case 1:
                    M21 = value.X;
                    M22 = value.Y;
                    break;
                case 2:
                    M31 = value.X;
                    M32 = value.Y;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(row));
            }
        }
    }

    public float this[int row, int column]
    {
        readonly get
        {
            return row switch
            {
                0 => column switch
                {
                    0 => M11,
                    1 => M12,
                    _ => throw new ArgumentOutOfRangeException(nameof(column)),
                },
                1 => column switch
                {
                    0 => M21,
                    1 => M22,
                    _ => throw new ArgumentOutOfRangeException(nameof(column)),
                },
                2 => column switch
                {
                    0 => M31,
                    1 => M32,
                    _ => throw new ArgumentOutOfRangeException(nameof(column)),
                },
                _ => throw new ArgumentOutOfRangeException(nameof(row)),
            };
        }
        set
        {
            switch (row)
            {
                case 0:
                    if (column == 0) M11 = value;
                    else if (column == 1) M12 = value;
                    else throw new ArgumentOutOfRangeException(nameof(column));
                    break;
                case 1:
                    if (column == 0) M21 = value;
                    else if (column == 1) M22 = value;
                    else throw new ArgumentOutOfRangeException(nameof(column));
                    break;
                case 2:
                    if (column == 0) M31 = value;
                    else if (column == 1) M32 = value;
                    else throw new ArgumentOutOfRangeException(nameof(column));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(row));
            }
        }
    }

    public static Matrix3x2 operator +(Matrix3x2 left, Matrix3x2 right) => new(
        left.M11 + right.M11, left.M12 + right.M12,
        left.M21 + right.M21, left.M22 + right.M22,
        left.M31 + right.M31, left.M32 + right.M32);

    public static bool operator ==(Matrix3x2 left, Matrix3x2 right) =>
        left.M11 == right.M11 && left.M12 == right.M12 &&
        left.M21 == right.M21 && left.M22 == right.M22 &&
        left.M31 == right.M31 && left.M32 == right.M32;

    public static bool operator !=(Matrix3x2 left, Matrix3x2 right) => !(left == right);

    public static Matrix3x2 operator *(Matrix3x2 left, Matrix3x2 right) => new(
        left.M11 * right.M11 + left.M12 * right.M21,
        left.M11 * right.M12 + left.M12 * right.M22,
        left.M21 * right.M11 + left.M22 * right.M21,
        left.M21 * right.M12 + left.M22 * right.M22,
        left.M31 * right.M11 + left.M32 * right.M21 + right.M31,
        left.M31 * right.M12 + left.M32 * right.M22 + right.M32);

    public static Matrix3x2 operator *(Matrix3x2 value, float scale) => new(
        value.M11 * scale, value.M12 * scale,
        value.M21 * scale, value.M22 * scale,
        value.M31 * scale, value.M32 * scale);

    public static Matrix3x2 operator -(Matrix3x2 left, Matrix3x2 right) => new(
        left.M11 - right.M11, left.M12 - right.M12,
        left.M21 - right.M21, left.M22 - right.M22,
        left.M31 - right.M31, left.M32 - right.M32);

    public static Matrix3x2 operator -(Matrix3x2 value) => new(
        -value.M11, -value.M12, -value.M21, -value.M22, -value.M31, -value.M32);

    public static Matrix3x2 Add(Matrix3x2 value1, Matrix3x2 value2) => value1 + value2;

    public static Matrix3x2 Create(float value) => new(value, value, value, value, value, value);

    public static Matrix3x2 Create(Vector2 value) => new(value.X, value.Y, value.X, value.Y, value.X, value.Y);

    public static Matrix3x2 Create(Vector2 x, Vector2 y, Vector2 z) =>
        new(x.X, x.Y, y.X, y.Y, z.X, z.Y);

    public static Matrix3x2 Create(float m11, float m12, float m21, float m22, float m31, float m32) =>
        new(m11, m12, m21, m22, m31, m32);

    public static Matrix3x2 CreateRotation(float radians)
    {
        (float s, float c) = GetRotationSinCos(radians);
        return new(c, s, -s, c, 0, 0);
    }

    public static Matrix3x2 CreateRotation(float radians, Vector2 centerPoint)
    {
        (float s, float c) = GetRotationSinCos(radians);
        float x = centerPoint.X * (1 - c) + centerPoint.Y * s;
        float y = centerPoint.Y * (1 - c) - centerPoint.X * s;
        return new(c, s, -s, c, x, y);
    }

    private static (float Sin, float Cos) GetRotationSinCos(float radians)
    {
        radians = MathF.IEEERemainder(radians, MathF.Tau);

        if (radians is > -RotationEpsilon and < RotationEpsilon)
            return (0, 1);
        if (radians is > (MathF.PI / 2 - RotationEpsilon) and < (MathF.PI / 2 + RotationEpsilon))
            return (1, 0);
        if (radians is < (-MathF.PI + RotationEpsilon) or > (MathF.PI - RotationEpsilon))
            return (0, -1);
        if (radians is > (-MathF.PI / 2 - RotationEpsilon) and < (-MathF.PI / 2 + RotationEpsilon))
            return (-1, 0);
        return MathF.SinCos(radians);
    }

    public static Matrix3x2 CreateScale(Vector2 scales) => CreateScale(scales.X, scales.Y);

    public static Matrix3x2 CreateScale(float xScale, float yScale) =>
        new(xScale, 0, 0, yScale, 0, 0);

    public static Matrix3x2 CreateScale(float xScale, float yScale, Vector2 centerPoint) =>
        new(xScale, 0, 0, yScale,
            centerPoint.X * (1 - xScale),
            centerPoint.Y * (1 - yScale));

    public static Matrix3x2 CreateScale(Vector2 scales, Vector2 centerPoint) =>
        CreateScale(scales.X, scales.Y, centerPoint);

    public static Matrix3x2 CreateScale(float scale) => CreateScale(scale, scale);

    public static Matrix3x2 CreateScale(float scale, Vector2 centerPoint) =>
        new(scale, 0, 0, scale, centerPoint.X * (1 - scale), centerPoint.Y * (1 - scale));

    public static Matrix3x2 CreateSkew(float radiansX, float radiansY) =>
        new(1, MathF.Tan(radiansY), MathF.Tan(radiansX), 1, 0, 0);

    public static Matrix3x2 CreateSkew(float radiansX, float radiansY, Vector2 centerPoint)
    {
        float xTan = MathF.Tan(radiansX);
        float yTan = MathF.Tan(radiansY);
        return new(1, yTan, xTan, 1, -centerPoint.Y * xTan, -centerPoint.X * yTan);
    }

    public static Matrix3x2 CreateTranslation(Vector2 position) =>
        new(1, 0, 0, 1, position.X, position.Y);

    public static Matrix3x2 CreateTranslation(float xPosition, float yPosition) =>
        new(1, 0, 0, 1, xPosition, yPosition);

    public static bool Invert(Matrix3x2 matrix, out Matrix3x2 result)
    {
        float determinant = matrix.M11 * matrix.M22 - matrix.M21 * matrix.M12;
        if (MathF.Abs(determinant) < InverseEpsilon)
        {
            result = Create(float.NaN);
            return false;
        }

        float inverseDeterminant = 1 / determinant;
        result = new(
            matrix.M22 * inverseDeterminant,
            -matrix.M12 * inverseDeterminant,
            -matrix.M21 * inverseDeterminant,
            matrix.M11 * inverseDeterminant,
            (matrix.M21 * matrix.M32 - matrix.M31 * matrix.M22) * inverseDeterminant,
            (matrix.M31 * matrix.M12 - matrix.M11 * matrix.M32) * inverseDeterminant);
        return true;
    }

    public static Matrix3x2 Lerp(Matrix3x2 matrix1, Matrix3x2 matrix2, float amount) => new(
        matrix1.M11 + (matrix2.M11 - matrix1.M11) * amount,
        matrix1.M12 + (matrix2.M12 - matrix1.M12) * amount,
        matrix1.M21 + (matrix2.M21 - matrix1.M21) * amount,
        matrix1.M22 + (matrix2.M22 - matrix1.M22) * amount,
        matrix1.M31 + (matrix2.M31 - matrix1.M31) * amount,
        matrix1.M32 + (matrix2.M32 - matrix1.M32) * amount);

    public static Matrix3x2 Multiply(Matrix3x2 value1, Matrix3x2 value2) => value1 * value2;
    public static Matrix3x2 Multiply(Matrix3x2 value1, float value2) => value1 * value2;
    public static Matrix3x2 Negate(Matrix3x2 value) => -value;
    public static Matrix3x2 Subtract(Matrix3x2 value1, Matrix3x2 value2) => value1 - value2;

    public override readonly bool Equals(object? obj) => obj is Matrix3x2 other && Equals(other);

    public readonly bool Equals(Matrix3x2 other) =>
        M11.Equals(other.M11) && M12.Equals(other.M12) &&
        M21.Equals(other.M21) && M22.Equals(other.M22) &&
        M31.Equals(other.M31) && M32.Equals(other.M32);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

    public readonly float GetDeterminant() => M11 * M22 - M21 * M12;

    public readonly float GetElement(int row, int column) => this[row, column];
    public readonly Vector2 GetRow(int index) => this[index];

    public override readonly string ToString() =>
        $"{{ {{M11:{M11} M12:{M12}}} {{M21:{M21} M22:{M22}}} {{M31:{M31} M32:{M32}}} }}";

    public readonly Matrix3x2 WithElement(int row, int column, float value)
    {
        Matrix3x2 result = this;
        result[row, column] = value;
        return result;
    }

    public readonly Matrix3x2 WithRow(int index, Vector2 value)
    {
        Matrix3x2 result = this;
        result[index] = value;
        return result;
    }
}
