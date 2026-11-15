// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NullableArrayReadbackCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NullableElementsUseNullableBoxingSemantics(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "NullableReadbackFixture", Source, "NullableReadbackFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    internal const string Source = """
        using System;
        namespace NullableReadbackFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                int?[] values = new int?[] { null, 42 };
                Array array = values;
                if (array.GetValue(0) != null) return 1;
                var boxed = array.GetValue(1);
                if (boxed is not int || (int)boxed != 42) return 2;
                Pair?[] pairs = new Pair?[] { null, new Pair { Text = "alive", Value = 42 } };
                Array structs = pairs;
                if (structs.GetValue(0) != null) return 3;
                var item = structs.GetValue(1); GC.Collect();
                if (item is not Pair || ((Pair)item).Text != "alive" || ((Pair)item).Value != 42) return 4;
                return input + 1;
            }
        }
        public struct Pair { public string Text; public int Value; }
        """;
}
