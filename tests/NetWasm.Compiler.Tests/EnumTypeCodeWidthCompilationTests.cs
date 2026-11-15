// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumTypeCodeWidthCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InterfaceDispatchReturnsTypeCodeForEachTarget(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "EnumTypeCodeWidthFixture",
            Source,
            "EnumTypeCodeWidthFixture.EntryPoint",
            optimize,
            target,
            17,
            []));

        Assert.Equal(0, observed);
    }

    private const string Source = """
        using System;

        namespace EnumTypeCodeWidthFixture;

        public enum Sample : int
        {
            One = 1,
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                IConvertible value = Sample.One;
                if (value.GetTypeCode() != TypeCode.Int32) return 1;
                var unknown = (Sample)42;
                if (unknown.ToString() != "42") return 2;
                if (unknown.ToString("X") != "0000002A") return 3;
                if (Enum.GetName(typeof(Sample), Sample.One) != "One") return 4;
                if (Enum.GetNames(typeof(Sample))[0] != "One") return 5;
                if (Enum.GetValues(typeof(Sample)).Length != 1) return 6;
                if (Enum.GetUnderlyingType(typeof(Sample)) != typeof(int)) return 7;
                if (!Enum.IsDefined(typeof(Sample), Sample.One)) return 8;
                if (!Enum.ToObject(typeof(Sample), 1).Equals(Sample.One)) return 9;
                if (Enum.Format(typeof(Sample), Sample.One, "G") != "One") return 10;
                if (Enum.Parse<Sample>("42") != unknown) return 15;
                if ((Sample)Enum.Parse(typeof(Sample), "-42") != (Sample)(-42)) return 16;
                if (!Enum.TryParse<Sample>(" +42 ", out var parsed) || parsed != unknown)
                    return 17;
                try
                {
                    _ = Enum.GetNames(null!);
                    return 11;
                }
                catch (ArgumentNullException)
                {
                }
                try
                {
                    _ = Enum.GetNames(typeof(int));
                    return 12;
                }
                catch (ArgumentException)
                {
                }
                try
                {
                    _ = Enum.Format(null!, Sample.One, "G");
                    return 13;
                }
                catch (ArgumentNullException)
                {
                }
                try
                {
                    _ = Enum.Format(typeof(int), 1, "G");
                    return 14;
                }
                catch (ArgumentException)
                {
                }
                return input - input;
            }
        }
        """;

}
