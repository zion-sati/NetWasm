// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ExceptionFilterValueCompilationTests
{
    [Theory]
    [MemberData(nameof(AsyncValueTaskResumptionCompilationTests.Configurations), MemberType = typeof(AsyncValueTaskResumptionCompilationTests))]
    [Trait("Issue", "41")]
    public void FilterCopiesPreserveStructValues(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "FilterValueFixture", Source, "FilterValueFixture.EntryPoint",
            optimize, target, 42, []));
        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        namespace FilterValueFixture;
        public struct Payload
        {
            public int Number;
            public string Text;
        }
        public sealed class Holder
        {
            public Payload Value;
        }
        public static class EntryPoint
        {
            private static Payload _value;
            private static Payload Value => _value;
            private static bool Accept(Payload value) => value.Number == 42 && value.Text == "ok";
            public static int Run(int input)
            {
                _value = new Payload { Number = input, Text = "ok" };
                var holder = new Holder { Value = _value };
                var local = _value;
                var array = new[] { _value };
                var total = 0;
                try { throw new Exception(); }
                catch (Exception) when (Accept(Value)) { total += 7; }
                try { throw new Exception(); }
                catch (Exception) when (Accept(_value)) { total += 7; }
                try { throw new Exception(); }
                catch (Exception) when (Accept(holder.Value)) { total += 7; }
                try { throw new Exception(); }
                catch (Exception) when (Accept(local)) { total += 7; }
                try { throw new Exception(); }
                catch (Exception) when (Accept(array[0])) { total += 7; }
                return total + Argument(_value);
            }
            private static int Argument(Payload value)
            {
                try { throw new Exception(); }
                catch (Exception) when (Accept(value)) { return 7; }
            }
        }
        """;
}
