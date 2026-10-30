namespace NetWasm.Compiler.Tests.Correctness;

// Vector2, Vector3, Vector4, Plane, Quaternion, Matrix3x2 and Matrix4x4 are upstream's sources,
// built on Vector128. Results compare bit for bit with desktop, which runs the same sources with
// hardware intrinsics, so each operation must keep upstream's evaluation order. Matrix4x4.Invert
// and Lerp use MultiplyAddEstimate, which desktop fuses where the hardware can and Wasm's SIMD128
// cannot, so their results compare rounded to 1/4096.
[Collection(CorrectnessTestGroup.Name)]
public sealed class NumericsVectorTypesCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NumericsVectorTypesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "NumericsVectorTypesCorpus",
            "NetWasm.Correctness.NumericsVectorTypes",
            """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.NumericsVectorTypes;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Twos(),
                    1 => Threes(),
                    2 => Fours(),
                    3 => Planes(),
                    4 => Quaternions(),
                    5 => Matrices3x2(),
                    >= 60 => Matrices4x4(input - 60),
                    _ => Specials(),
                };

                private static int Twos()
                {
                    var a = new Vector2(1.5f, -2.25f);
                    var b = new Vector2(-0.5f, 4f);
                    var h = 17;
                    h = Mix(h, a + b); h = Mix(h, a - b); h = Mix(h, a * b); h = Mix(h, a / b); h = Mix(h, a * 3f);
                    h = Mix(h, -a); h = Mix(h, Vector2.Dot(a, b)); h = Mix(h, a.Length()); h = Mix(h, a.LengthSquared());
                    h = Mix(h, Vector2.Distance(a, b)); h = Mix(h, Vector2.Normalize(a)); h = Mix(h, Vector2.Lerp(a, b, 0.3f));
                    h = Mix(h, Vector2.Min(a, b)); h = Mix(h, Vector2.Max(a, b)); h = Mix(h, Vector2.Clamp(a, b, a + b));
                    h = Mix(h, Vector2.Abs(a)); h = Mix(h, Vector2.SquareRoot(Vector2.Abs(a))); h = Mix(h, Vector2.Reflect(a, Vector2.Normalize(b)));
                    h = Mix(h, Vector2.Transform(a, Matrix3x2.CreateRotation(0.7f))); h = Mix(h, Vector2.TransformNormal(a, Matrix4x4.CreateScale(2f, 3f, 4f)));
                    h = Mix(h, Vector2.Transform(a, Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f)));
                    h = Mix(h, Vector2.FusedMultiplyAdd(a, b, a)); h = Mix(h, Vector2.Cross(a, b));
                    h = Mix(h, a == b ? 1 : 0); h = Mix(h, a.Equals(new Vector2(1.5f, -2.25f)) ? 1 : 0);
                    return Mix(h, a.ToString().Length);
                }

                private static int Threes()
                {
                    var a = new Vector3(1.5f, -2.25f, 3.125f);
                    var b = new Vector3(-0.5f, 4f, 0.75f);
                    var h = 19;
                    h = Mix(h, a + b); h = Mix(h, a - b); h = Mix(h, a * b); h = Mix(h, a / b); h = Mix(h, b / 3f);
                    h = Mix(h, Vector3.Dot(a, b)); h = Mix(h, Vector3.Cross(a, b)); h = Mix(h, a.Length());
                    h = Mix(h, Vector3.Distance(a, b)); h = Mix(h, Vector3.DistanceSquared(a, b)); h = Mix(h, Vector3.Normalize(a));
                    h = Mix(h, Vector3.Lerp(a, b, 0.6f)); h = Mix(h, Vector3.Reflect(a, Vector3.Normalize(b)));
                    h = Mix(h, Vector3.Transform(a, Matrix4x4.CreateFromYawPitchRoll(0.4f, -0.2f, 1.1f)));
                    h = Mix(h, Vector3.TransformNormal(a, Matrix4x4.CreateRotationZ(0.9f)));
                    h = Mix(h, Vector3.Transform(a, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f)));
                    h = Mix(h, Vector3.Clamp(a, -Vector3.One, Vector3.One)); h = Mix(h, Vector3.Min(a, b)); h = Mix(h, Vector3.Max(a, b));
                    return Mix(h, Vector3.Abs(-a));
                }

                private static int Fours()
                {
                    var a = new Vector4(1.5f, -2.25f, 3.125f, -7f);
                    var b = new Vector4(-0.5f, 4f, 0.75f, 2f);
                    var h = 23;
                    h = Mix(h, a + b); h = Mix(h, a - b); h = Mix(h, a * b); h = Mix(h, a / b);
                    h = Mix(h, Vector4.Dot(a, b)); h = Mix(h, a.Length()); h = Mix(h, Vector4.Normalize(a));
                    h = Mix(h, Vector4.Lerp(a, b, 0.25f)); h = Mix(h, Vector4.Distance(a, b));
                    h = Mix(h, Vector4.Transform(a, Matrix4x4.CreatePerspectiveFieldOfView(1f, 1.5f, 0.1f, 100f)));
                    h = Mix(h, Vector4.Transform(new Vector3(1, 2, 3), Matrix4x4.CreateTranslation(4, 5, 6)));
                    h = Mix(h, Vector4.Transform(a, Quaternion.Normalize(new Quaternion(1, 2, 3, 4))));
                    h = Mix(h, Vector4.Clamp(a, -Vector4.One, Vector4.One)); h = Mix(h, Vector4.SquareRoot(Vector4.Abs(a)));
                    return Mix(h, a == b ? 1 : 0);
                }

                private static int Planes()
                {
                    var p = Plane.CreateFromVertices(new Vector3(1, 0, 0), new Vector3(0, 2, 0), new Vector3(0, 0, 3));
                    var h = 29;
                    h = Mix(h, p.Normal); h = Mix(h, p.D); h = Mix(h, Plane.Normalize(new Plane(1, 2, 3, 4)));
                    h = Mix(h, Plane.Dot(p, new Vector4(1, 2, 3, 1))); h = Mix(h, Plane.DotCoordinate(p, new Vector3(4, 5, 6)));
                    h = Mix(h, Plane.DotNormal(p, new Vector3(4, 5, 6)));
                    h = Mix(h, Plane.Transform(p, Matrix4x4.CreateRotationX(0.5f)).Normal);
                    return Mix(h, Plane.Transform(p, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f)).D);
                }

                private static int Quaternions()
                {
                    var a = Quaternion.CreateFromYawPitchRoll(0.3f, -0.6f, 1.2f);
                    var b = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 1, 0)), 0.9f);
                    var h = 31;
                    h = Mix(h, a); h = Mix(h, b); h = Mix(h, a * b); h = Mix(h, a + b); h = Mix(h, a - b);
                    h = Mix(h, Quaternion.Slerp(a, b, 0.35f)); h = Mix(h, Quaternion.Lerp(a, b, 0.35f));
                    h = Mix(h, Quaternion.Concatenate(a, b)); h = Mix(h, Quaternion.Conjugate(a)); h = Mix(h, Quaternion.Inverse(a));
                    h = Mix(h, Quaternion.Normalize(a * 3f)); h = Mix(h, Quaternion.Dot(a, b)); h = Mix(h, a.Length());
                    h = Mix(h, Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationY(0.7f)));
                    h = Mix(h, a / b);
                    return Mix(h, a.IsIdentity ? 1 : 0);
                }

                private static int Matrices3x2()
                {
                    var m = Matrix3x2.CreateRotation(0.5f, new Vector2(1, 2)) * Matrix3x2.CreateScale(2, 3);
                    var h = 37;
                    h = Mix(h, m); h = Mix(h, Matrix3x2.CreateSkew(0.1f, 0.2f)); h = Mix(h, Matrix3x2.CreateTranslation(3, 4));
                    h = Mix(h, Matrix3x2.Invert(m, out var inverse) ? 1 : 0); h = Mix(h, inverse);
                    h = Mix(h, m.GetDeterminant()); h = Mix(h, Matrix3x2.Lerp(m, inverse, 0.4f)); h = Mix(h, m + inverse);
                    h = Mix(h, m - inverse); h = Mix(h, m * 2f); h = Mix(h, -m); h = Mix(h, m.Translation);
                    return Mix(h, m == inverse ? 1 : 0);
                }

                private static int Matrices4x4(int part)
                {
                    var m = Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f) * Matrix4x4.CreateTranslation(1, 2, 3) *
                        Matrix4x4.CreateScale(1.5f);
                    var h = 41;
                    switch (part)
                    {
                        case 0: return Mix(h, m);
                        case 1: return Rounded(Mix(h, Matrix4x4.Invert(m, out var inverse) ? 1 : 0), inverse);
                        case 2: return Mix(h, m.GetDeterminant());
                        case 3: return Mix(h, Matrix4x4.Transpose(m));
                        case 4: Matrix4x4.Invert(m, out var other); return Rounded(h, m * other);
                        case 5:
                            h = Mix(h, Matrix4x4.Decompose(m, out var scale, out var rotation, out var translation) ? 1 : 0);
                            return Mix(Mix(Mix(h, scale), rotation), translation);
                        case 6: return Mix(h, Matrix4x4.CreateLookAt(new Vector3(1, 2, 3), Vector3.Zero, Vector3.UnitY));
                        case 7: return Mix(h, Matrix4x4.CreateOrthographicOffCenter(-1, 2, -3, 4, 0.5f, 50));
                        case 8: return Mix(h, Matrix4x4.CreateBillboard(Vector3.One, Vector3.Zero, Vector3.UnitY, Vector3.UnitZ));
                        case 9: return Mix(h, Matrix4x4.CreateReflection(new Plane(0, 1, 0, 2)));
                        case 10: Matrix4x4.Invert(m, out var third); return Rounded(h, Matrix4x4.Lerp(m, third, 0.5f));
                        case 11: return Mix(h, Matrix4x4.CreateShadow(Vector3.Normalize(new Vector3(1, -1, 1)), new Plane(0, 1, 0, 0)));
                        case 12: return Mix(h, Matrix4x4.Transform(m, Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f)));
                        default: return Mix(h, m.IsIdentity ? 1 : 0);
                    }
                }

                private static int Specials()
                {
                    var h = 43;
                    h = Mix(h, Vector3.Normalize(Vector3.Zero)); h = Mix(h, new Vector4(float.NaN, -0f, float.PositiveInfinity, 1) * 2);
                    h = Mix(h, Vector2.Max(new Vector2(-0f, float.NaN), new Vector2(0f, 1f)));
                    h = Mix(h, Vector2.Min(new Vector2(-0f, float.NaN), new Vector2(0f, 1f)));
                    h = Mix(h, Matrix4x4.Invert(new Matrix4x4(), out var singular) ? 1 : 0); h = Mix(h, singular);
                    h = Mix(h, Quaternion.Normalize(Quaternion.Zero));
                    var span = new float[5];
                    new Vector4(1, 2, 3, 4).CopyTo(span, 1);
                    foreach (var value in span) h = Mix(h, value);
                    return Mix(h, Vector3.One.GetHashCode() == Vector3.One.GetHashCode() ? 1 : 0);
                }

                private static int Mix(int hash, float value) => unchecked(hash * 31 + BitConverter.SingleToInt32Bits(value));
                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);
                private static int Mix(int hash, Vector2 value) => Mix(Mix(hash, value.X), value.Y);
                private static int Mix(int hash, Vector3 value) => Mix(Mix(Mix(hash, value.X), value.Y), value.Z);
                private static int Mix(int hash, Vector4 value) => Mix(Mix(Mix(Mix(hash, value.X), value.Y), value.Z), value.W);
                private static int Mix(int hash, Quaternion value) => Mix(Mix(Mix(Mix(hash, value.X), value.Y), value.Z), value.W);
                private static int Mix(int hash, Plane value) => Mix(Mix(hash, value.Normal), value.D);
                private static int Mix(int hash, Matrix3x2 m) => Mix(Mix(Mix(Mix(Mix(Mix(hash, m.M11), m.M12), m.M21), m.M22), m.M31), m.M32);
                private static int Mix(int hash, Matrix4x4 m)
                {
                    for (var row = 0; row < 4; row++)
                        for (var column = 0; column < 4; column++)
                            hash = Mix(hash, m[row, column]);
                    return hash;
                }

                private static int Rounded(int hash, Matrix4x4 m)
                {
                    for (var row = 0; row < 4; row++)
                        for (var column = 0; column < 4; column++)
                            hash = Mix(hash, (int)MathF.Round(m[row, column] * 4096));
                    return hash;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 7, .. Enumerable.Range(60, 14)])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
