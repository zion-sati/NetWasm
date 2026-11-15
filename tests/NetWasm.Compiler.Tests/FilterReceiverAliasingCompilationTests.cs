// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FilterReceiverAliasingCompilationTests
{
    public static TheoryData<bool, WasmTarget, int> Configurations => new()
    {
        { false, WasmTarget.Wasm32, 0 },
        { false, WasmTarget.Wasm32, 1 },
        { false, WasmTarget.Wasm32, 2 },
        { false, WasmTarget.Wasm32, 3 },
        { false, WasmTarget.Wasm64, 0 },
        { false, WasmTarget.Wasm64, 1 },
        { false, WasmTarget.Wasm64, 2 },
        { false, WasmTarget.Wasm64, 3 },
        { true, WasmTarget.Wasm32, 0 },
        { true, WasmTarget.Wasm32, 1 },
        { true, WasmTarget.Wasm32, 2 },
        { true, WasmTarget.Wasm32, 3 },
        { true, WasmTarget.Wasm64, 0 },
        { true, WasmTarget.Wasm64, 1 },
        { true, WasmTarget.Wasm64, 2 },
        { true, WasmTarget.Wasm64, 3 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    [Trait("Issue", "40")]
    public void CapturedReceiverPreservesCallerVisibleMutation(bool optimize, WasmTarget target, int mode)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario("FilterReceiverFixture", Source,
            "FilterReceiverFixture.EntryPoint", optimize, target, mode, []));
        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        namespace FilterReceiverFixture;

        public struct Mutable
        {
            public int Number;
            public string Text;
            public void Update(bool fail)
            {
                try
                {
                    Number = 42;
                    Text = "updated";
                    if (fail) throw new Exception();
                }
                catch (Exception) when (Number == 42)
                {
                    Number = 43;
                }
            }
        }

        public sealed class Reference
        {
            public int Number;
            public void Update()
            {
                try { Number = 42; }
                catch (Exception) when (Number == 42) { Number = -1; }
            }
        }

        public static class EntryPoint
        {
            public static int Run(int mode)
            {
                var value = new Mutable { Number = 7, Text = "initial" };
                if (mode == 2)
                {
                    var reference = new Reference();
                    reference.Update();
                    return reference.Number;
                }
                if (mode == 3)
                {
                    UpdateByReference(ref value);
                    return value.Number;
                }
                value.Update(mode == 1);
                return value.Text == "updated" ? value.Number - (mode == 1 ? 1 : 0) : -1;
            }

            private static void UpdateByReference(ref Mutable value)
            {
                try { value.Number = 42; }
                catch (Exception) when (value.Number == 42) { value.Number = -1; }
            }
        }
        """;
}
