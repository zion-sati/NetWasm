// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class InstanceAccessorInliningCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void TrivialReferenceGettersPreserveNullAndVirtualDispatch(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"InstanceAccessorInlining_{optimize}_{target}",
            Source,
            "EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Optimization = FinalWasmOptimization.O3,
        });

        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var item = new Item(input);
                var sum = 0;
                for (var index = 0; index < 64; index++)
                {
                    sum += item.Value;
                }
                if (sum != input * 64) return 1;

                Item? missing = null;
                try
                {
                    _ = missing!.Value;
                    return 2;
                }
                catch (NullReferenceException)
                {
                }

                BaseItem polymorphic = new DerivedItem(input);
                if (polymorphic.Value != input + 1) return 3;
                return 42;
            }
        }

        public sealed class Item(int value)
        {
            private readonly int _value = value;
            public int Value => _value;
        }

        public class BaseItem
        {
            public virtual int Value => -1;
        }

        public sealed class DerivedItem(int value) : BaseItem
        {
            private readonly int _value = value;
            public override int Value => _value + 1;
        }
        """;
}
