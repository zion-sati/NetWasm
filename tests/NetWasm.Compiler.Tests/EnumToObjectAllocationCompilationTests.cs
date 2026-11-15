// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumToObjectAllocationCompilationTests
{
    [Theory]
    [InlineData(0, false, WasmTarget.Wasm32)]
    [InlineData(0, false, WasmTarget.Wasm64)]
    [InlineData(0, true, WasmTarget.Wasm32)]
    [InlineData(0, true, WasmTarget.Wasm64)]
    [InlineData(1, false, WasmTarget.Wasm32)]
    [InlineData(1, false, WasmTarget.Wasm64)]
    [InlineData(1, true, WasmTarget.Wasm32)]
    [InlineData(1, true, WasmTarget.Wasm64)]
    [InlineData(2, false, WasmTarget.Wasm32)]
    [InlineData(2, false, WasmTarget.Wasm64)]
    [InlineData(2, true, WasmTarget.Wasm32)]
    [InlineData(2, true, WasmTarget.Wasm64)]
    public void ToObjectRetainsClosedEnumConversionDispatchAndRejectsOpenDefinitions(
        int scenario, bool optimized, WasmTarget target)
    {
        var body = scenario switch
        {
            0 => "return Convert.ToInt64(Enum.ToObject(typeof(Code), 7)) == 7 ? 42 : 1;",
            1 => "return Convert.ToInt64(Enum.ToObject(typeof(Outer<int>.Code), 7)) == 7 ? 42 : 1;",
            _ => """
                try { Enum.ToObject(typeof(Outer<>.Code), 7); return 1; }
                catch (ArgumentException) { return 42; }
                """,
        };
        // Only the intrinsic may allocate an enum here. No enum value boxing,
        // enum arrays, or GetValues call can supply its dispatch receiver root.
        var source = $$"""
            using System;
            public enum Code { Seven = 7 }
            public class Outer<T> { public enum Code : ushort { Seven = 7 } }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{body}}
                }
            }
            """;
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "EnumToObjectAllocation", source, "EntryPoint", optimized, target, 0, [])));
    }
}
