// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Generated runtime-async methods (RuntimeAsyncShapeGenerator) against desktop .NET, from Release IL at no optimization
// and at O3 (where the structurer's speed retries run), and from Debug IL at no optimization (the default Debug build,
// which marks lines in stack-trace frames) and at O3. The seeds are a sample of a sweep over 160 seeds at O3 (120 from
// Release IL, 40 from Debug IL) and 84 at no optimization, the four that failed it with a retry since removed (15, 30,
// 31, 44: blocks not emitted, a leave with no continuation, a wrong result and a hang), and 33, whose Debug build trapped
// at no optimization when a directly called method's catch passed an exception by without rewinding the stack-trace
// frames its handler marks lines in; a defect in the runtime async lowering, its stack typer, the structurer or the
// exception frames shows here as a compile failure, a trap or a different trace.
public sealed class RuntimeAsyncShapeDifferentialTests(CorrectnessTestRunner runner)
{
    private static readonly int[] Seeds = [3, 7, 11, 15, 19, 23, 30, 31, 33, 42, 44];

    public static TheoryData<int, FinalWasmOptimization, string> Cases()
    {
        var cases = new TheoryData<int, FinalWasmOptimization, string>();
        foreach (var seed in Seeds)
        {
            cases.Add(seed, FinalWasmOptimization.None, "Release-Wasm32-Direct-Linked");
            cases.Add(seed, FinalWasmOptimization.O3, "Release-Wasm32-Direct-Linked");
            cases.Add(seed, FinalWasmOptimization.None, "Debug-Wasm32-Direct-Linked");
            cases.Add(seed, FinalWasmOptimization.O3, "Debug-Wasm32-Direct-Linked");
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedRuntimeAsyncMethodsMatchDesktop(int seed, FinalWasmOptimization optimization, string cell)
    {
        var namespaceName = $"NetWasm.Correctness.RuntimeAsyncShapes.Seed{seed}";
        runner.Run(new CorpusFixture(
            $"RuntimeAsyncShapes{seed}", namespaceName, RuntimeAsyncShapeGenerator.Source(seed, namespaceName),
            [.. RuntimeAsyncShapeGenerator.Inputs])
        {
            CaseId = "runtime-async.generated-shapes",
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare NetWasm's runtime async lowering of generated methods with desktop .NET's.",
            Optimization = optimization,
            // A Debug build records stack traces with lines, as the SDK's Debug configuration does.
            EmitStackTrace = cell.StartsWith("Debug", StringComparison.Ordinal),
        });
    }

    [Fact]
    public void ASeedAlwaysGeneratesTheSameMethods()
    {
        var first = RuntimeAsyncShapeGenerator.Source(19, "Shapes");
        var second = RuntimeAsyncShapeGenerator.Source(19, "Shapes");

        Assert.Equal(first, second);
        Assert.Contains("await", first, StringComparison.Ordinal);
        Assert.NotEqual(first, RuntimeAsyncShapeGenerator.Source(20, "Shapes"));
    }
}
