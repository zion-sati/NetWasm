// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumNamesArrayCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void EnumNamesRetainArrayInterfaceDispatchWithoutOtherArrayAllocations(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "EnumNamesArray", Source, "EntryPoint", optimized, target, 0, [])));
    }

    // No newarr or array type token can supply the intrinsic's allocation root.
    private const string Source = """
        using System;
        using System.Collections.Generic;
        public enum Code { Seven = 7, Nine = 9 }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                IEnumerable<string> names = Enum.GetNames<Code>();
                var count = 0;
                foreach (var name in names)
                {
                    if (name != (count == 0 ? "Seven" : "Nine")) return 1;
                    count++;
                }
                if (count != 2) return 2;
                names = Enum.GetNames(typeof(Code));
                count = 0;
                foreach (var name in names)
                {
                    if (name != (count == 0 ? "Seven" : "Nine")) return 3;
                    count++;
                }
                return count == 2 ? 42 : 4;
            }
        }
        """;
}
