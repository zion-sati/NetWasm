namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PortableMatrixCompatibilityTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ScalarMatrixOperationsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "PortableMatrixCompatibility",
            "NetWasm.Correctness.PortableMatrices",
            """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.PortableMatrices;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => RowsAndElements(),
                    1 => VectorTransforms(),
                    2 => Inversion(),
                    3 => Decomposition(),
                    4 => Projection(),
                    5 => PlaneTransforms(),
                    6 => QuaternionRoundTrip(),
                    7 => ViewsAndViewport(),
                    _ => UnsafePointerFamily(),
                };

                private static int RowsAndElements()
                {
                    var matrix = Matrix4x4.Create(new Matrix3x2(1, 2, 3, 4, 5, 6));
                    matrix = matrix.WithRow(2, new Vector4(7, 8, 9, 10)).WithElement(3, 3, 11);
                    return Quantize(matrix.GetElement(2, 1) + matrix.GetRow(3).W + matrix.Translation.Y);
                }

                private static int VectorTransforms()
                {
                    var matrix = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateTranslation(5, 6, 7);
                    var v2 = Vector2.Transform(new Vector2(1, 2), matrix);
                    var n2 = Vector2.TransformNormal(new Vector2(1, 2), matrix);
                    var v3 = Vector3.Transform(new Vector3(1, 2, 3), matrix);
                    var n3 = Vector3.TransformNormal(new Vector3(1, 2, 3), matrix);
                    var v4 = Vector4.Transform(new Vector4(1, 2, 3, 1), matrix);
                    return Quantize(v2.X + n2.Y + v3.Z + n3.X + v4.W);
                }

                private static int Inversion()
                {
                    var matrix = Matrix4x4.CreateFromYawPitchRoll(0.2f, -0.3f, 0.4f)
                        * Matrix4x4.CreateScale(2, 3, 4)
                        * Matrix4x4.CreateTranslation(5, -6, 7);
                    var succeeded = Matrix4x4.Invert(matrix, out var inverse);
                    var identity = matrix * inverse;
                    var singular = Matrix4x4.Invert(Matrix4x4.CreateScale(0), out _);
                    return Quantize(identity.M11 + identity.M22 + identity.M33 + identity.M44)
                        + (succeeded ? 1 : 0) + (singular ? 100 : 0);
                }

                private static int Decomposition()
                {
                    var source = Matrix4x4.CreateScale(2, 3, 4)
                        * Matrix4x4.CreateFromYawPitchRoll(0.2f, 0.3f, -0.1f)
                        * Matrix4x4.CreateTranslation(5, 6, 7);
                    var succeeded = Matrix4x4.Decompose(source, out var scale, out var rotation, out var translation);
                    return Quantize(scale.X + scale.Y + scale.Z + rotation.W + translation.X + translation.Y + translation.Z)
                        + (succeeded ? 1 : 0);
                }

                private static int Projection()
                {
                    var right = Matrix4x4.CreatePerspectiveFieldOfView(0.8f, 1.5f, 0.1f, 100f);
                    var left = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(0.8f, 1.5f, 0.1f, 100f);
                    var ortho = Matrix4x4.CreateOrthographicOffCenterLeftHanded(-2, 3, -4, 5, 0.1f, 20f);
                    return Quantize(right.M34 + left.M34 + ortho.M41 + ortho.M43);
                }

                private static int PlaneTransforms()
                {
                    var plane = Plane.Normalize(new Plane(new Vector3(1, 2, 3), -4));
                    var matrixValue = Plane.Transform(plane, Matrix4x4.CreateTranslation(5, 6, 7));
                    var quaternionValue = Plane.Transform(plane, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
                    return Quantize(matrixValue.D + quaternionValue.Normal.X + quaternionValue.Normal.Z);
                }

                private static int QuaternionRoundTrip()
                {
                    var source = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.3f, -0.7f, 1.1f));
                    var result = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateFromQuaternion(source));
                    return Quantize(MathF.Abs(Quaternion.Dot(source, result)));
                }

                private static int ViewsAndViewport()
                {
                    var look = Matrix4x4.CreateLookToLeftHanded(new Vector3(1, 2, 3), Vector3.UnitZ, Vector3.UnitY);
                    var billboard = Matrix4x4.CreateBillboardLeftHanded(Vector3.Zero, new Vector3(0, 0, -5), Vector3.UnitY, Vector3.UnitZ);
                    var viewport = Matrix4x4.CreateViewportLeftHanded(10, 20, 640, 480, 0, 1);
                    return Quantize(look.M43 + billboard.M33 + viewport.M11 + viewport.M22 + viewport.M33);
                }

                private static unsafe int UnsafePointerFamily()
                {
                    var cells = stackalloc Cell[2];
                    cells[0].Value = 7;
                    Add(ref cells[0].Value, 5);

                    var values = new[] { 11, 13 };
                    fixed (int* first = values)
                    {
                        Add(ref *first, 3);
                        cells[1].Value = ReadPointer(first + 1);
                        return (cells[0].Increment() * 100) + first[0] + cells[1].Value;
                    }
                }

                private static void Add(ref int value, int delta) => value += delta;

                private static unsafe int ReadPointer(int* value) => *value;

                private struct Cell
                {
                    public int Value;

                    public int Increment() => ++Value;
                }

                private static int Quantize(float value) => (int)MathF.Round(value * 1000);

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
