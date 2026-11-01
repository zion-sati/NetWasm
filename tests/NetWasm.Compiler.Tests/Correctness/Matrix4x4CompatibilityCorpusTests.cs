namespace NetWasm.Compiler.Tests.Correctness;

public sealed class Matrix4x4CompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void Matrix4x4AndDependentScalarNumericsMatchDesktop()
    {
        runner.Run(new(
            "Matrix4x4CompatibilityCorpus",
            "NetWasm.Correctness.Matrix4x4Compatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
    }

    private const string Source = """
        using System;
        using System.Numerics;

        namespace NetWasm.Correctness.Matrix4x4Compatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => Result(ConstructionAndAccessors(), input),
                1 => Result(Arithmetic(), input),
                2 => Result(TransposeAndInversion(), input),
                3 => Result(Decompose(), input),
                4 => Result(TransformFactories(), input),
                5 => Result(ProjectionAndCameraFactories(), input),
                6 => Result(DependentTransforms(), input),
                7 => Result(EdgeContracts(), input),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int Result(bool passed, int input) =>
                passed ? 100 + input : -(100 + input);

            private static bool Near(float actual, float expected, float tolerance = 0.0005f) =>
                MathF.Abs(actual - expected) <= tolerance;

            private static bool Near(Vector2 actual, Vector2 expected) =>
                Near(actual.X, expected.X) && Near(actual.Y, expected.Y);

            private static bool Near(Vector3 actual, Vector3 expected) =>
                Near(actual.X, expected.X) && Near(actual.Y, expected.Y) &&
                Near(actual.Z, expected.Z);

            private static bool Near(Vector4 actual, Vector4 expected) =>
                Near(actual.X, expected.X) && Near(actual.Y, expected.Y) &&
                Near(actual.Z, expected.Z) && Near(actual.W, expected.W);

            private static bool Near(Matrix4x4 actual, Matrix4x4 expected)
            {
                return Near(actual.M11, expected.M11) && Near(actual.M12, expected.M12) &&
                    Near(actual.M13, expected.M13) && Near(actual.M14, expected.M14) &&
                    Near(actual.M21, expected.M21) && Near(actual.M22, expected.M22) &&
                    Near(actual.M23, expected.M23) && Near(actual.M24, expected.M24) &&
                    Near(actual.M31, expected.M31) && Near(actual.M32, expected.M32) &&
                    Near(actual.M33, expected.M33) && Near(actual.M34, expected.M34) &&
                    Near(actual.M41, expected.M41) && Near(actual.M42, expected.M42) &&
                    Near(actual.M43, expected.M43) && Near(actual.M44, expected.M44);
            }

            private static bool Finite(Matrix4x4 value)
            {
                return float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
                    float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
                    float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
                    float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
                    float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
                    float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
                    float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
                    float.IsFinite(value.M43) && float.IsFinite(value.M44);
            }

            private static bool ConstructionAndAccessors()
            {
                var matrix = new Matrix4x4(
                    1, 2, 3, 4,
                    5, 6, 7, 8,
                    9, 10, 11, 12,
                    13, 14, 15, 16);
                var rows = Matrix4x4.Create(
                    new Vector4(1, 2, 3, 4),
                    new Vector4(5, 6, 7, 8),
                    new Vector4(9, 10, 11, 12),
                    new Vector4(13, 14, 15, 16));
                var scalar = Matrix4x4.Create(7);
                var repeatedRow = Matrix4x4.Create(new Vector4(7));
                var fromThreeByTwo = Matrix4x4.Create(new Matrix3x2(1, 2, 3, 4, 5, 6));

                var changed = matrix.WithElement(0, 1, 22).WithRow(3, new Vector4(17, 18, 19, 20));
                var mutable = matrix;
                mutable.Translation = new Vector3(21, 22, 23);
                mutable.X = new Vector4(24, 25, 26, 27);

                return Near(matrix, rows) &&
                    matrix.M11 == 1 && matrix.M44 == 16 &&
                    Near(matrix[0], new Vector4(1, 2, 3, 4)) &&
                    Near(matrix[3, 2], 15) &&
                    Near(scalar, Matrix4x4.Create(7)) &&
                    Near(repeatedRow, scalar) &&
                    Near(fromThreeByTwo, new Matrix4x4(
                        1, 2, 0, 0,
                        3, 4, 0, 0,
                        0, 0, 1, 0,
                        5, 6, 0, 1)) &&
                    changed.M12 == 22 && changed.W == new Vector4(17, 18, 19, 20) &&
                    mutable.M11 == 24 && mutable.M42 == 22 &&
                    Matrix4x4.Identity.IsIdentity && !matrix.IsIdentity &&
                    Near(matrix.Translation, new Vector3(13, 14, 15)) &&
                    Near(matrix.X, new Vector4(1, 2, 3, 4)) &&
                    Near(matrix.Y, new Vector4(5, 6, 7, 8)) &&
                    Near(matrix.Z, new Vector4(9, 10, 11, 12)) &&
                    Near(matrix.W, new Vector4(13, 14, 15, 16));
            }

            private static bool Arithmetic()
            {
                var left = new Matrix4x4(
                    1, 2, 3, 4,
                    5, 6, 7, 8,
                    9, 10, 11, 12,
                    13, 14, 15, 16);
                var right = Matrix4x4.Create(2);
                var expectedAdd = Matrix4x4.Create(
                    3, 4, 5, 6,
                    7, 8, 9, 10,
                    11, 12, 13, 14,
                    15, 16, 17, 18);
                var expectedSubtract = Matrix4x4.Create(
                    -1, 0, 1, 2,
                    3, 4, 5, 6,
                    7, 8, 9, 10,
                    11, 12, 13, 14);
                var expectedScaled = Matrix4x4.Create(
                    2, 4, 6, 8,
                    10, 12, 14, 16,
                    18, 20, 22, 24,
                    26, 28, 30, 32);

                return Near(left + right, expectedAdd) &&
                    Near(Matrix4x4.Add(left, right), expectedAdd) &&
                    Near(left - right, expectedSubtract) &&
                    Near(Matrix4x4.Subtract(left, right), expectedSubtract) &&
                    Near(left * 2, expectedScaled) &&
                    Near(Matrix4x4.Multiply(left, 2), expectedScaled) &&
                    Near(-right, Matrix4x4.Create(-2)) &&
                    Near(Matrix4x4.Negate(right), Matrix4x4.Create(-2)) &&
                    Near(Matrix4x4.Lerp(left, right, 0.5f), Matrix4x4.Create(
                        1.5f, 2, 2.5f, 3,
                        3.5f, 4, 4.5f, 5,
                        5.5f, 6, 6.5f, 7,
                        7.5f, 8, 8.5f, 9)) &&
                    Near(Matrix4x4.Multiply(Matrix4x4.Identity, left), left) &&
                    left.Equals(left) && left != right;
            }

            private static bool TransposeAndInversion()
            {
                var value = new Matrix4x4(
                    1, 2, 3, 4,
                    0, 1, 4, 5,
                    5, 6, 0, 7,
                    1, 0, 0, 1);
                var transposed = Matrix4x4.Transpose(value);
                var expectedTranspose = new Matrix4x4(
                    1, 0, 5, 1,
                    2, 1, 6, 0,
                    3, 4, 0, 0,
                    4, 5, 7, 1);

                var affine = Matrix4x4.CreateScale(2, 3, 4) *
                    Matrix4x4.CreateTranslation(5, 6, 7);
                var invertible = Matrix4x4.Invert(affine, out var inverse);
                var singular = new Matrix4x4(
                    1, 2, 3, 4,
                    1, 2, 3, 4,
                    0, 0, 0, 0,
                    0, 0, 0, 0);
                var singularResult = Matrix4x4.Invert(singular, out var ignored);

                return Near(transposed, expectedTranspose) &&
                    Near(Matrix4x4.Transpose(transposed), value) &&
                    invertible && Near(affine * inverse, Matrix4x4.Identity) &&
                    Near(inverse.M11, 0.5f) && Near(inverse.M22, 1f / 3f) &&
                    Near(inverse.M33, 0.25f) && Near(inverse.Translation, new Vector3(-2.5f, -2, -1.75f)) &&
                    !singularResult && float.IsNaN(ignored.M11);
            }

            private static bool Decompose()
            {
                var scale = new Vector3(2, 3, 4);
                var rotation = Quaternion.CreateFromYawPitchRoll(0.2f, -0.3f, 0.4f);
                var translation = new Vector3(5, 6, 7);
                var matrix = Matrix4x4.CreateScale(scale) *
                    Matrix4x4.CreateFromQuaternion(rotation) *
                    Matrix4x4.CreateTranslation(translation);

                var succeeded = Matrix4x4.Decompose(
                    matrix,
                    out var actualScale,
                    out var actualRotation,
                    out var actualTranslation);
                var rotationAgreement = MathF.Abs(Quaternion.Dot(rotation, actualRotation));
                var degenerate = Matrix4x4.Decompose(
                    Matrix4x4.CreateScale(0, 2, 3),
                    out _, out _, out _);

                return succeeded && Near(actualScale, scale) &&
                    rotationAgreement > 0.999f && Near(actualTranslation, translation) &&
                    !degenerate;
            }

            private static bool TransformFactories()
            {
                var quarterTurn = MathF.PI / 2;
                var quaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitY, quarterTurn);
                var rotationX = Matrix4x4.CreateRotationX(quarterTurn);
                var rotationY = Matrix4x4.CreateRotationY(quarterTurn);
                var rotationZ = Matrix4x4.CreateRotationZ(quarterTurn);
                var rotationXCentre = Matrix4x4.CreateRotationX(quarterTurn, new Vector3(1, 2, 3));
                var rotationYCentre = Matrix4x4.CreateRotationY(quarterTurn, new Vector3(1, 2, 3));
                var rotationZCentre = Matrix4x4.CreateRotationZ(quarterTurn, new Vector3(1, 2, 3));
                var axis = Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, quarterTurn);
                var fromQuaternion = Matrix4x4.CreateFromQuaternion(quaternion);
                var yawPitchRoll = Matrix4x4.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f);
                var scaleVector = Matrix4x4.CreateScale(new Vector3(2, 3, 4));
                var scaleCentre = Matrix4x4.CreateScale(2, 3, 4, new Vector3(1, 2, 3));
                var scaleUniformCentre = Matrix4x4.CreateScale(2, new Vector3(1, 2, 3));
                var translationVector = Matrix4x4.CreateTranslation(new Vector3(5, 6, 7));
                var translationScalars = Matrix4x4.CreateTranslation(5, 6, 7);
                var reflection = Matrix4x4.CreateReflection(new Plane(Vector3.UnitY, -2));
                var shadow = Matrix4x4.CreateShadow(new Vector3(1, -1, 1), new Plane(Vector3.UnitY, 0));
                var billboard = Matrix4x4.CreateBillboard(
                    Vector3.Zero, new Vector3(0, 0, 5), Vector3.UnitY, Vector3.UnitZ);
                var billboardLeft = Matrix4x4.CreateBillboardLeftHanded(
                    Vector3.Zero, new Vector3(0, 0, 5), Vector3.UnitY, Vector3.UnitZ);
                var constrained = Matrix4x4.CreateConstrainedBillboard(
                    Vector3.Zero, new Vector3(0, 0, 5), Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ);
                var constrainedLeft = Matrix4x4.CreateConstrainedBillboardLeftHanded(
                    Vector3.Zero, new Vector3(0, 0, 5), Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ);

                return Near(Vector3.Transform(Vector3.UnitY, rotationX), new Vector3(0, 0, 1)) &&
                    Near(Vector3.Transform(Vector3.UnitZ, rotationY), new Vector3(1, 0, 0)) &&
                    Near(Vector3.Transform(Vector3.UnitX, rotationZ), new Vector3(0, 1, 0)) &&
                    Near(rotationX.Translation, Vector3.Zero) &&
                    Near(rotationXCentre.Translation, new Vector3(1, 5, -2)) &&
                    Near(rotationYCentre.Translation, new Vector3(4, 2, 2)) &&
                    Near(rotationZCentre.Translation, new Vector3(-1, 3, 3)) &&
                    Near(axis, fromQuaternion) &&
                    Finite(yawPitchRoll) && Near(scaleVector.M11, 2) &&
                    Near(scaleVector.M22, 3) && Near(scaleVector.M33, 4) &&
                    Finite(scaleCentre) && Finite(scaleUniformCentre) &&
                    Near(translationVector, translationScalars) &&
                    Finite(reflection) && Finite(shadow) && Finite(billboard) &&
                    Finite(billboardLeft) && Finite(constrained) && Finite(constrainedLeft);
            }

            private static bool ProjectionAndCameraFactories()
            {
                var lookAt = Matrix4x4.CreateLookAt(
                    new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
                var lookAtLeft = Matrix4x4.CreateLookAtLeftHanded(
                    new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
                var lookTo = Matrix4x4.CreateLookTo(
                    new Vector3(0, 0, 5), -Vector3.UnitZ, Vector3.UnitY);
                var lookToLeft = Matrix4x4.CreateLookToLeftHanded(
                    new Vector3(0, 0, 5), Vector3.UnitZ, Vector3.UnitY);
                var orthographic = Matrix4x4.CreateOrthographic(10, 20, 1, 101);
                var orthographicLeft = Matrix4x4.CreateOrthographicLeftHanded(10, 20, 1, 101);
                var orthographicOff = Matrix4x4.CreateOrthographicOffCenter(-5, 5, -10, 10, 1, 101);
                var orthographicOffLeft = Matrix4x4.CreateOrthographicOffCenterLeftHanded(-5, 5, -10, 10, 1, 101);
                var perspective = Matrix4x4.CreatePerspective(10, 20, 1, 101);
                var perspectiveLeft = Matrix4x4.CreatePerspectiveLeftHanded(10, 20, 1, 101);
                var fieldOfView = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 2, 1, 101);
                var fieldOfViewLeft = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(MathF.PI / 2, 2, 1, 101);
                var perspectiveOff = Matrix4x4.CreatePerspectiveOffCenter(-5, 5, -10, 10, 1, 101);
                var perspectiveOffLeft = Matrix4x4.CreatePerspectiveOffCenterLeftHanded(-5, 5, -10, 10, 1, 101);
                var viewport = Matrix4x4.CreateViewport(10, 20, 640, 480, 0, 1);
                var viewportLeft = Matrix4x4.CreateViewportLeftHanded(10, 20, 640, 480, 0, 1);
                var world = Matrix4x4.CreateWorld(new Vector3(1, 2, 3), -Vector3.UnitZ, Vector3.UnitY);

                return Finite(lookAt) && Finite(lookAtLeft) && Finite(lookTo) &&
                    Finite(lookToLeft) && Finite(orthographic) && Finite(orthographicLeft) &&
                    Finite(orthographicOff) && Finite(orthographicOffLeft) && Finite(perspective) &&
                    Finite(perspectiveLeft) && Finite(fieldOfView) && Finite(fieldOfViewLeft) &&
                    Finite(perspectiveOff) && Finite(perspectiveOffLeft) && Finite(viewport) &&
                    Finite(viewportLeft) && Finite(world) &&
                    Near(orthographic.M11, 0.2f) && Near(orthographic.M22, 0.1f) &&
                    Near(orthographic.M44, 1) && Near(perspective.M34, -1) &&
                    Near(perspective.M44, 0) && Near(viewport.M11, 320) &&
                    Near(viewport.M22, -240) && Near(viewport.M44, 1) &&
                    Near(lookAt.M44, 1) && Near(world.Translation, new Vector3(1, 2, 3));
            }

            private static bool DependentTransforms()
            {
                var matrix = Matrix4x4.CreateScale(2, 3, 4) *
                    Matrix4x4.CreateTranslation(5, 6, 7);
                var vector2 = new Vector2(1, 2);
                var vector3 = new Vector3(1, 2, 3);
                var vector4 = new Vector4(1, 2, 3, 1);
                var quaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
                var plane = new Plane(Vector3.UnitY, -2);
                var transformedPlane = Plane.Transform(plane, matrix);
                var identityPlane = Plane.Transform(plane, Matrix4x4.Identity);
                var fromIdentity = Quaternion.CreateFromRotationMatrix(Matrix4x4.Identity);
                var fromRotation = Quaternion.CreateFromRotationMatrix(
                    Matrix4x4.CreateFromQuaternion(quaternion));
                var transformedMatrix = Matrix4x4.Transform(Matrix4x4.Identity, quaternion);

                return Near(Vector2.Transform(vector2, matrix), new Vector2(7, 12)) &&
                    Near(Vector2.TransformNormal(vector2, matrix), new Vector2(2, 6)) &&
                    Near(Vector3.Transform(vector3, matrix), new Vector3(7, 12, 19)) &&
                    Near(Vector3.TransformNormal(vector3, matrix), new Vector3(2, 6, 12)) &&
                    Near(Vector4.Transform(new Vector2(1, 2), matrix), new Vector4(7, 12, 7, 1)) &&
                    Near(Vector4.Transform(vector3, matrix), new Vector4(7, 12, 19, 1)) &&
                    Near(Vector4.Transform(vector4, matrix), new Vector4(7, 12, 19, 1)) &&
                    Near(identityPlane.Normal, plane.Normal) && Near(identityPlane.D, plane.D) &&
                    float.IsFinite(transformedPlane.D) &&
                    MathF.Abs(Quaternion.Dot(fromIdentity, Quaternion.Identity)) > 0.999f &&
                    MathF.Abs(Quaternion.Dot(fromRotation, quaternion)) > 0.999f &&
                    Near(transformedMatrix, Matrix4x4.CreateFromQuaternion(quaternion));
            }

            private static bool EdgeContracts()
            {
                var value = Matrix4x4.Identity;
                var equivalent = Matrix4x4.Identity;
                var different = Matrix4x4.CreateTranslation(1, 0, 0);
                var nan = Matrix4x4.Create(float.NaN);
                var invalidRow = false;
                var invalidColumn = false;

                try { _ = value[-1]; }
                catch (ArgumentOutOfRangeException) { invalidRow = true; }
                try { _ = value[0, 4]; }
                catch (ArgumentOutOfRangeException) { invalidColumn = true; }

                return value.Equals(equivalent) && value.Equals((object)equivalent) &&
                    value.GetHashCode() == equivalent.GetHashCode() &&
                    value != different && !nan.Equals(value) &&
                    invalidRow && invalidColumn;
            }
        }
        """;
}
