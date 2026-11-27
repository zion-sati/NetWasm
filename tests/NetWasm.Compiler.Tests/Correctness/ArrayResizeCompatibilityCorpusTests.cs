// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Array.Resize is upstream's: an array already of the size stays the same instance, a null one becomes a new array, the
// elements move as they are (a string[] resized as object[], an int[] seen as uint[]), and a negative size throws.
// Value, reference and reference-holding struct elements, growing and shrinking, at every preset against desktop.
public sealed class ArrayResizeCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ResizeMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ArrayResizeCompatibilityCorpus" + optimization,
            "NetWasm.Correctness.ArrayResize",
            """
            using System;

            namespace NetWasm.Correctness.ArrayResize;

            public struct Pair
            {
                public string Name;
                public int Value;
            }

            public static class EntryPoint
            {
                private static int _hash;

                private static void Mix(int value) => _hash = unchecked((_hash ^ value) * 16777619);

                private static void Mix(string? text)
                {
                    if (text is null) { Mix(-1); return; }
                    Mix(text.Length);
                    foreach (var c in text) Mix(c);
                }

                public static int Run(int input)
                {
                    _hash = unchecked((int)2166136261);
                    var numbers = new int[5 + input];
                    for (var i = 0; i < numbers.Length; i++) numbers[i] = i * 7 + input;
                    var before = numbers;
                    Array.Resize(ref numbers, numbers.Length);
                    Mix(ReferenceEquals(before, numbers) ? 1 : 0);
                    Array.Resize(ref numbers, numbers.Length * 2 + 1);
                    Mix(ReferenceEquals(before, numbers) ? 1 : 0);
                    Mix(numbers.Length);
                    foreach (var number in numbers) Mix(number);
                    Array.Resize(ref numbers, 3);
                    foreach (var number in numbers) Mix(number);

                    var texts = new string[4];
                    for (var i = 0; i < texts.Length; i++) texts[i] = "t" + (i + input);
                    for (var round = 0; round < 6; round++)
                    {
                        Array.Resize(ref texts, texts.Length + 3);
                        texts[^1] = "new" + round;
                    }
                    foreach (var text in texts) Mix(text);

                    object[] covariant = new string[] { "a", "b" + input };
                    Array.Resize(ref covariant, 5);
                    Mix(covariant.GetType() == typeof(object[]) ? 1 : 0);
                    foreach (var item in covariant) Mix(item as string);

                    uint[] reinterpreted = (uint[])(object)new int[] { -1, input, -3 };
                    Array.Resize(ref reinterpreted, 4);
                    foreach (var item in reinterpreted) Mix((int)(item % 1000));

                    var pairs = new Pair[2];
                    pairs[0] = new Pair { Name = "p" + input, Value = 1 };
                    pairs[1] = new Pair { Name = "q", Value = 2 };
                    Array.Resize(ref pairs, 7);
                    foreach (var pair in pairs) { Mix(pair.Name); Mix(pair.Value); }

                    int[]? missing = null;
                    Array.Resize(ref missing, 3 + input);
                    Mix(missing.Length);

                    try
                    {
                        Array.Resize(ref numbers, -1 - input);
                        Mix(100);
                    }
                    catch (ArgumentOutOfRangeException error)
                    {
                        Mix(error.ParamName);
                    }
                    return _hash;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 5])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = optimization != FinalWasmOptimization.None,
        });
}
