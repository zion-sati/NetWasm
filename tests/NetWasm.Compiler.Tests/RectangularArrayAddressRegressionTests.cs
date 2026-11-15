// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RectangularArrayAddressRegressionTests
{
    [Theory]
    [MemberData(nameof(AsyncValueTaskResumptionCompilationTests.Configurations), MemberType = typeof(AsyncValueTaskResumptionCompilationTests))]
    [Trait("Issue", "43")]
    public void GenericArrayAddressPreservesValueAndReferenceElements(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario("ArrayAddressFixture", Source,
            "ArrayAddressFixture.EntryPoint", optimize, target, 42, []));
        Assert.Equal(42, result);
    }

    private const string Source = """
        namespace ArrayAddressFixture;
        public struct Payload { public int Number; public string Text; }
        public static class EntryPoint
        {
            public static int Run(int value)
            {
                var values = new Payload[1,1];
                At(values) = new Payload { Number = value, Text = "ok" };
                var references = new string[1,1];
                At(references) = "hello";
                var numbers = new int[1,1];
                At(numbers) = value;
                return Read(in values[0,0]) == value && ReadOnlyAt(references) == "hello"
                    && ReadOnlyAt(numbers) == value ? value : -1;
            }
            private static ref T At<T>(T[,] values) => ref values[0,0];
            private static ref readonly T ReadOnlyAt<T>(T[,] values) => ref values[0,0];
            private static int Read(in Payload value) => value.Text == "ok" ? value.Number : -1;
        }
        """;
}
