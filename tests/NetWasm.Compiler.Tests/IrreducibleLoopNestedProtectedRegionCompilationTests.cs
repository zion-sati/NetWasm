// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Synchronous counterparts of LoopNestedProtectedRegionAsyncCompilationTests: goto builds a loop
// with two entries, so the protected code before a nested try/finally is an irreducible
// dispatcher whose exits are an early return and the fallthrough into the nested try. Expected
// values come from desktop .NET.
public sealed class IrreducibleLoopNestedProtectedRegionCompilationTests
{
    public static TheoryData<bool, WasmTarget> Cases() => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void IrreducibleLoopsWithNestedProtectedRegionsMatchDesktop(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(42, executor.Execute(new CompilationScenario(
            $"IrreducibleNestedProtected{(optimize ? "Release" : "Debug")}{target}",
            Source,
            "IrreducibleNestedProtected.EntryPoint",
            optimize,
            target,
            41,
            [])));
    }

    private const string Source = """
        using System;

        namespace IrreducibleNestedProtected;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                long h1 = 17;
                foreach (var n in new[] { 0, 1 }) h1 = h1 * 31 + G1(n);
                long h2 = 17;
                for (var m = 0; m < 8; m++) h2 = h2 * 31 + G2(m & 1, (m >> 1) & 1, (m >> 2) & 1);
                long h3 = 17;
                for (var m = 0; m < 16; m++) h3 = h3 * 31 + G3(m & 1, (m >> 1) & 1, (m >> 2) & 1, (m >> 3) & 1);
                long h4 = 17;
                foreach (var n in new[] { 0, 2, 3 }) h4 = h4 * 31 + G4(n);
                var mismatches = (h1 == 122893L ? 0 : 1) | (h2 == 18106882389385L ? 0 : 2) |
                    (h3 == 7842508611604646805L ? 0 : 4) | (h4 == 565131L ? 0 : 8);
                return mismatches == 0 ? input + 1 : 1000 + mismatches;
            }

            // before a nested try/finally.
            private static int G1(int n)
            {
                int s = 0, i = 0;
                if (n > 0) goto Middle;
            Top:
                if (i >= 3) goto Done;
                s += 100;
            Middle:
                try
                {
                    if (i == 7) return -1;
                    s += 1;
                    try { s += 10; } finally { s += 1000; }
                }
                catch (InvalidOperationException) { throw; }
                i++;
                goto Top;
            Done:
                return s;
            }

            // Overlapping goto arms inside a try code part that falls into a nested try.
            private static int G2(int a, int b, int c)
            {
                int s = 0;
                try
                {
                    if (a > 0) goto X;
                    s += 1;
                    if (b > 0) goto Y;
                    s += 2;
                X:
                    s += 4;
                    if (c > 0) goto Z;
                Y:
                    s += 8;
                Z:
                    s += 16;
                    try { s += 32; } finally { s += 64; }
                }
                catch (InvalidOperationException) { throw; }
                return s;
            }

            // Overlapping arms with a leave among them, before a nested try.
            private static int G3(int a, int b, int c, int d)
            {
                int s = 0;
                try
                {
                    if (a > 0) goto X;
                    s += 1;
                    if (b > 0) goto Y;
                    s += 2;
                X:
                    s += 4;
                    if (d > 0) return -s;
                    if (c > 0) goto Z;
                Y:
                    s += 8;
                Z:
                    s += 16;
                    try { s += 32; } finally { s += 64; }
                }
                catch (InvalidOperationException) { throw; }
                return s;
            }

            // A try/catch-all whose try returns early, then holds a nested try/finally.
            private static int G4(int n)
            {
                int s = 0, i = 0;
                if (n > 1) goto Middle;
            Top:
                if (i >= 2) goto Done;
                s += 7;
            Middle:
                try
                {
                    if (i == 9) return -2;
                    try { s += 3; } finally { s *= 2; }
                }
                catch { s += 1; }
                i++;
                goto Top;
            Done:
                return s;
            }
        }
        """;
}
