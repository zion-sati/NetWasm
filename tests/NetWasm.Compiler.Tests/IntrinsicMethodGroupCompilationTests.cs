// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Methods the compiler expands as intrinsics at call sites still need a function when a
// delegate or function pointer takes their address (Humanizer passes input.HasFlag to Where).
public sealed class IntrinsicMethodGroupCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DelegatesToIntrinsicMethodsInvokeTheirIntrinsicBehaviour(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            $"IntrinsicMethodGroup{(optimize ? "Release" : "Debug")}{target}", Source, "IntrinsicMethodGroupFixture.EntryPoint", optimize, target, 41, [])));
    }

    // Every expected value matches desktop .NET.
    private const string Source = """
        using System;
        using System.Collections.Generic;
        namespace IntrinsicMethodGroupFixture;
        [Flags] public enum Access { None = 0, Read = 1, Write = 2, Execute = 4 }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Enum granted = Access.Read | Access.Write;
                Func<Enum, bool> has = granted.HasFlag;
                var found = new List<Access>();
                foreach (var flag in new[] { Access.Read, Access.Write, Access.Execute })
                {
                    if (has(flag)) found.Add(flag);
                }
                if (found.Count != 2 || found[0] != Access.Read || found[1] != Access.Write) return 1;

                Func<double, double> floor = Math.Floor;
                Func<double, double> abs = Math.Abs;
                Func<int, int> absInt = Math.Abs;
                if (floor(2.75) != 2.0 || abs(-1.5) != 1.5 || absInt(-7) != 7) return 2;

                var value = Access.Execute;
                Func<string> name = value.ToString;
                object boxed = Access.Write;
                Func<string> virtualName = boxed.ToString;
                if (name() != "Execute" || virtualName() != "Write") return 3;

                Func<Enum, bool> stale = granted.HasFlag;
                try { stale(null!); return 4; }
                catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        """;
}
