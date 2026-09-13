// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Scalar adaptation of dotnet/runtime System.Numerics.Plane.cs.

namespace System.Numerics;

public struct Plane : IEquatable<Plane>
{
    public Vector3 Normal;
    public float D;

    public Plane(float x, float y, float z, float d)
    {
        Normal = new Vector3(x, y, z);
        D = d;
    }

    public Plane(Vector3 normal, float d)
    {
        Normal = normal;
        D = d;
    }

    public Plane(Vector4 value)
    {
        Normal = new Vector3(value.X, value.Y, value.Z);
        D = value.W;
    }

    public static Plane Create(Vector4 value) => new(value);
    public static Plane Create(Vector3 normal, float d) => new(normal, d);
    public static Plane Create(float x, float y, float z, float d) => new(x, y, z, d);

    public static Plane CreateFromVertices(Vector3 point1, Vector3 point2, Vector3 point3)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(point2 - point1, point3 - point1));
        return new Plane(normal, -Vector3.Dot(normal, point1));
    }

    public static float Dot(Plane plane, Vector4 value) =>
        plane.Normal.X * value.X +
        plane.Normal.Y * value.Y +
        plane.Normal.Z * value.Z +
        plane.D * value.W;

    public static float DotCoordinate(Plane plane, Vector3 value) =>
        plane.Normal.X * value.X +
        plane.Normal.Y * value.Y +
        plane.Normal.Z * value.Z +
        plane.D;

    public static float DotNormal(Plane plane, Vector3 value) =>
        plane.Normal.X * value.X +
        plane.Normal.Y * value.Y +
        plane.Normal.Z * value.Z;

    public static Plane Normalize(Plane value)
    {
        float lengthSquared =
            value.Normal.X * value.Normal.X +
            value.Normal.Y * value.Normal.Y +
            value.Normal.Z * value.Normal.Z;

        if (float.IsPositiveInfinity(lengthSquared))
        {
            return default;
        }

        float scale = 1 / MathF.Sqrt(lengthSquared);
        return new Plane(value.Normal * scale, value.D * scale);
    }

    public static Plane Transform(Plane plane, Matrix4x4 matrix)
    {
        Matrix4x4.Invert(matrix, out var inverse);
        return new(
            plane.Normal.X * inverse.M11 + plane.Normal.Y * inverse.M12 + plane.Normal.Z * inverse.M13 + plane.D * inverse.M14,
            plane.Normal.X * inverse.M21 + plane.Normal.Y * inverse.M22 + plane.Normal.Z * inverse.M23 + plane.D * inverse.M24,
            plane.Normal.X * inverse.M31 + plane.Normal.Y * inverse.M32 + plane.Normal.Z * inverse.M33 + plane.D * inverse.M34,
            plane.Normal.X * inverse.M41 + plane.Normal.Y * inverse.M42 + plane.Normal.Z * inverse.M43 + plane.D * inverse.M44);
    }

    public static Plane Transform(Plane plane, Quaternion rotation) =>
        new(Vector3.Transform(plane.Normal, rotation), plane.D);

    public static bool operator ==(Plane left, Plane right) =>
        left.Normal == right.Normal && left.D == right.D;

    public static bool operator !=(Plane left, Plane right) => !(left == right);

    public override readonly bool Equals(object? obj) => obj is Plane other && Equals(other);

    public readonly bool Equals(Plane other) =>
        Normal.X.Equals(other.Normal.X) &&
        Normal.Y.Equals(other.Normal.Y) &&
        Normal.Z.Equals(other.Normal.Z) &&
        D.Equals(other.D);

    public override readonly int GetHashCode() => HashCode.Combine(Normal, D);

    public override readonly string ToString() => $"{{Normal:{Normal} D:{D}}}";
}
