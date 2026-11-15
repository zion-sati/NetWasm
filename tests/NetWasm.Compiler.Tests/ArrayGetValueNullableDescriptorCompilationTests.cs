// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Array.GetValue boxes Nullable<T> elements; a Nullable<T> that only appears outside arrays
// (Markdig's TableColumnAlign? property) has no boxed T and must not stop compilation.
public sealed class ArrayGetValueNullableDescriptorCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void GetValueCompilesBesideNullableValuesNoArrayHolds(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            $"ArrayGetValueNullableDescriptor{(optimize ? "Release" : "Debug")}{target}", Source, "ArrayGetValueNullableDescriptorFixture.EntryPoint", optimize, target, 41, [])));
    }

    // Every expected value matches desktop .NET.
    private const string Source = """
        using System;
        namespace ArrayGetValueNullableDescriptorFixture;
        public enum Align { Left, Center, Right }
        public enum Weight { Light, Bold }
        public sealed class Column { public Align? Alignment { get; set; } }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var column = new Column { Alignment = Align.Center };
                Array numbers = new int[] { input, 7 };
                if ((int)numbers.GetValue(1)! != 7 || column.Alignment != Align.Center) return 1;

                // A Nullable<T> array still boxes its elements as T.
                Array weights = new Weight?[] { Weight.Bold, null };
                if (weights.GetValue(0) is not Weight.Bold || weights.GetValue(1) is not null) return 2;
                return (int)numbers.GetValue(0)! + 1;
            }
        }
        """;
}
