// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NestedCatchLoopCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CatchBranchesInsideALoopPreserveTheirEnclosingFinally(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            
            public sealed class Scope : IDisposable
            {
                public int Disposals;
                public void Dispose() { Disposals++; }
            }
            
            public static class EntryPoint
            {
                private static void Fail(int value)
                {
                    if (value == 0) throw new ArgumentNullException("data");
                    if (value == 1) throw new ArgumentOutOfRangeException("offset");
                    if (value == 2) throw new ArgumentOutOfRangeException("count");
                    throw new ArgumentException();
                }
            
                public static int Run(int input)
                {
                    var scope = new Scope();
                    var result = RunCore(input, scope);
                    return scope.Disposals == 1 ? result : -1;
                }

                private static int RunCore(int input, Scope scope)
                {
                    using var owned = scope;
                    for (var kind = 0; kind < 2; kind++)
                    {
                        var observed = 0;
                        try { Fail(0); }
                        catch (ArgumentNullException ex) { if (ex.ParamName == "data") observed |= 1; }
                        try { Fail(1); }
                        catch (ArgumentOutOfRangeException ex) { if (ex.ParamName == "offset") observed |= 2; }
                        try { Fail(2); }
                        catch (ArgumentOutOfRangeException ex) { if (ex.ParamName == "count") observed |= 4; }
                        try { Fail(3); }
                        catch (ArgumentException) { observed |= 8; }
                        if (observed != 15) return 100 + kind * 16 + observed;
                    }
                    return input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "NestedCatchLoopFixture", source, "EntryPoint", optimize, target, 42, []));

        Assert.Equal(42, result);
    }
}
