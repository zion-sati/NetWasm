// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ComponentModelContractCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesComponentModelContracts(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "ComponentModelContractFixture",
            Source,
            "ComponentModelContractFixture.EntryPoint",
            optimize,
            target,
            0,
            ImmutableArray<string>.Empty));

        Assert.Equal(31, observed);
    }

    private const string Source = """
        using System;
        using System.ComponentModel;

        namespace ComponentModelContractFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var observed = 0;
                var delayed = new MutableDefaultValueAttribute(typeof(int), "42");
                try
                {
                    _ = delayed.Value;
                }
                catch (ArgumentException error)
                {
                    if (error.Message == "Runtime instantiation of this attribute is not allowed.")
                    {
                        observed |= 1;
                    }
                }

                delayed.ReplaceValue(42);
                if ((int)delayed.Value! == 42 && delayed.Equals(new DefaultValueAttribute(42)))
                {
                    observed |= 2;
                }

                var first = new EditorBrowsableAttribute((EditorBrowsableState)42);
                var second = new EditorBrowsableAttribute((EditorBrowsableState)42);
                if (first.Equals(second) && first.GetHashCode() == second.GetHashCode())
                {
                    observed |= 4;
                }

                try
                {
                    _ = new InvalidEnumArgumentException("value", 42, null!);
                }
                catch (ArgumentNullException)
                {
                    observed |= 8;
                }

                var description = new DescriptionAttribute("netwasm");
                if (description.Equals(new DescriptionAttribute("netwasm")) &&
                    description.GetHashCode() == new DescriptionAttribute("netwasm").GetHashCode())
                {
                    observed |= 16;
                }

                return observed + input - input;
            }

            private sealed class MutableDefaultValueAttribute(Type type, string value) :
                DefaultValueAttribute(type, value)
            {
                internal void ReplaceValue(object? value) => SetValue(value);
            }
        }
        """;
}
