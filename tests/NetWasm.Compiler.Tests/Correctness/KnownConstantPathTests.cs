// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// RuntimeHelpers.IsKnownConstant picks CoreLib's paths for constant arguments: a string's first or last character
// against a constant char, Contains of a one-character literal, IndexOfAny of a case pair, and Matrix4x4 and
// Matrix3x2 with constant rows, columns and shuffle lanes (Matrix4x4.Invert reaches PackedSimd.Shuffle). Each path
// gives what the general one gives, as on desktop .NET. The matrices invert exactly in binary floating point, so
// every vector path's result is the same bits.
public sealed class KnownConstantPathTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void ConstantArgumentPathsGiveTheGeneralPathsResults() => Run(new(
        "KnownConstantPaths", "NetWasm.Correctness.KnownConstantPaths",
        """
        using System;
        using System.Numerics;
        namespace NetWasm.Correctness.KnownConstantPaths;
        public static class EntryPoint
        {
            public static int RunDesktop(int input) => Run(input);
            public static int Trace() => 0;

            private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

            private static int Hash(Matrix4x4 m) => unchecked(
                Bits(m.M11) * 3 + Bits(m.M12) * 5 + Bits(m.M13) * 7 + Bits(m.M14) * 11 +
                Bits(m.M21) * 13 + Bits(m.M22) * 17 + Bits(m.M23) * 19 + Bits(m.M24) * 23 +
                Bits(m.M31) * 29 + Bits(m.M32) * 31 + Bits(m.M33) * 37 + Bits(m.M34) * 41 +
                Bits(m.M41) * 43 + Bits(m.M42) * 47 + Bits(m.M43) * 53 + Bits(m.M44) * 59) & 0x7fffffff;

            public static int Run(int input)
            {
                var text = input < 100 ? "abcAbc" : "";
                switch (input)
                {
                    case 0:
                        return (text.EndsWith('c') ? 1 : 0) | (text.EndsWith('x') ? 2 : 0) | ("".EndsWith('c') ? 4 : 0);
                    case 1:
                        return (text.StartsWith('a') ? 1 : 0) | (text.StartsWith('b') ? 2 : 0) | ("".StartsWith('a') ? 4 : 0);
                    case 2:
                        return (text.Contains("b") ? 1 : 0) | (text.Contains("z") ? 2 : 0) | (text.Contains("bc") ? 4 : 0);
                    case 3:
                        return text.AsSpan().IndexOfAny('A', 'a') * 100 + text.AsSpan().IndexOfAny('X', 'x') + 50 +
                            text.AsSpan().LastIndexOfAny('B', 'b') * 1000;
                    case 4:
                    {
                        var matrix = Matrix4x4.CreateScale(2, 4, 0.5f) * Matrix4x4.CreateTranslation(1, 2, 3);
                        var inverted = Matrix4x4.Invert(matrix, out var inverse);
                        return (inverted ? 1 : 0) + Hash(inverse) % 1000000 * 10 + Hash(inverse * matrix) % 7;
                    }
                    case 5:
                    {
                        var matrix = new Matrix4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
                        var row = matrix.GetRow(1);
                        matrix[2, 3] = 99;
                        matrix = matrix.WithRow(0, new Vector4(-1, -2, -3, -4));
                        return (int)(matrix[2, 3] + matrix[0, 1] * 10 + row.Z * 100 + matrix.GetElement(3, 0) * 1000);
                    }
                    default:
                    {
                        var matrix = Matrix3x2.CreateScale(4, 0.25f) * Matrix3x2.CreateTranslation(8, -2);
                        var inverted = Matrix3x2.Invert(matrix, out var inverse);
                        matrix[1, 0] = 6;
                        return (inverted ? 1 : 0) + (Bits(inverse.M11) ^ Bits(inverse.M22) ^ Bits(inverse.M31) ^ Bits(inverse.M32)) % 100000 * 10 +
                            (int)matrix[1, 0] * 1000000;
                    }
                }
            }
        }
        """, [.. Enumerable.Range(0, 7)]));
}
