// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ReferenceArrayElementAddressCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ReferenceElementAddressesCheckArrayCovariance(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"ReferenceArrayElementAddress_{optimize}_{target}", Source,
            "ReferenceArrayElementAddressFixture.EntryPoint", optimize, target, 41, []));
        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;
        using System.Threading;

        namespace ReferenceArrayElementAddressFixture;

        public static class EntryPoint
        {
            static ref readonly object ReadOnlyFirst(object[] values) => ref values[0];

            public static int Run(int input)
            {
                // A writable reference to a reference-type element passes the exact-type check.
                var names = new string?[2];
                Volatile.Write(ref names[1], "x");
                if (Volatile.Read(ref names[1]) != "x" || names[0] != null) return 1;

                // A covariant array rejects a writable element reference.
                object[] covariant = new string[] { "s" };
                try
                {
                    ref object element = ref covariant[0];
                    return 2;
                }
                catch (ArrayTypeMismatchException)
                {
                }

                // A readonly element reference needs no check, and value-type elements have none.
                if (!ReferenceEquals(ReadOnlyFirst(covariant), covariant[0])) return 3;
                var numbers = new int[1];
                Interlocked.Increment(ref numbers[0]);
                return numbers[0] == 1 ? input + 1 : 4;
            }
        }
        """;
}
