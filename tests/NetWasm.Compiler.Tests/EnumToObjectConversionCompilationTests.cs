// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Enum.ToObject(Type, object) converts every boxed integer, char, bool and enum into enums of every underlying type
// as desktop .NET does (truncating and sign- or zero-extending), and rejects anything else; the expected results are
// desktop .NET's own, computed here.
public sealed class EnumToObjectConversionCompilationTests
{
    public enum EI1 : sbyte { }
    public enum EU1 : byte { }
    public enum EI2 : short { }
    public enum EU2 : ushort { }
    public enum EI4 : int { }
    public enum EU4 : uint { }
    public enum EI8 : long { }
    public enum EU8 : ulong { }

    private static readonly (string Expression, object? Value)[] Sources =
    [
        ("true", true), ("'\\u00ff'", 'ÿ'), ("(sbyte)-1", (sbyte)-1), ("(byte)200", (byte)200),
        ("(short)-2", (short)-2), ("(ushort)60000", (ushort)60000), ("-3", -3), ("4000000000u", 4000000000u),
        ("-4L", -4L), ("18000000000000000000ul", 18000000000000000000ul), ("(EI4)(-5)", (EI4)(-5)),
        ("(EU1)250", (EU1)250), ("\"7\"", "7"), ("1.5", 1.5),
    ];

    private static readonly Type[] Targets =
        [typeof(EI1), typeof(EU1), typeof(EI2), typeof(EU2), typeof(EI4), typeof(EU4), typeof(EI8), typeof(EU8)];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConvertsEveryBoxedIntegralSourceIntoEveryUnderlyingTypeAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "EnumToObjectConversion", Source(), "EntryPoint", optimized, target, 0, [])));
    }

    private static string Source()
    {
        var checks = new StringBuilder();
        var check = 100;
        foreach (var (expression, value) in Sources)
        {
            foreach (var type in Targets)
            {
                check++;
                var name = type.Name;
                string expected;
                try
                {
                    expected = "Bits(Enum.ToObject(typeof(" + name + "), " + expression + ")) == " +
                        Bits(Enum.ToObject(type, value!)).ToString(CultureInfo.InvariantCulture) + "L && Enum.ToObject(typeof(" +
                        name + "), " + expression + ") is " + name;
                    checks.AppendLine(CultureInfo.InvariantCulture, $"        if (!({expected})) return {check};");
                }
                catch (ArgumentException)
                {
                    checks.AppendLine(CultureInfo.InvariantCulture,
                        $"        try {{ Enum.ToObject(typeof({name}), {expression}); return {check}; }} catch (ArgumentException) {{ }}");
                }
            }
        }
        checks.AppendLine("        try { Enum.ToObject(typeof(EI4), (object)null!); return 99; } catch (ArgumentNullException) { }");
        var enums = string.Join('\n', Targets.Select(type =>
            $"public enum {type.Name} : {Enum.GetUnderlyingType(type).FullName} {{ }}"));
        return $$"""
            using System;
            {{enums}}
            public static class EntryPoint
            {
                private static long Bits(object value) => value switch
                {
                    EI1 v => (sbyte)v, EU1 v => (byte)v, EI2 v => (short)v, EU2 v => (ushort)v,
                    EI4 v => (int)v, EU4 v => (uint)v, EI8 v => (long)v, EU8 v => unchecked((long)(ulong)v),
                    _ => long.MinValue,
                };

                public static int Run(int input)
                {
            {{checks}}
                    return 42;
                }
            }
            """;
    }

    private static long Bits(object value) => value switch
    {
        EI1 v => (sbyte)v,
        EU1 v => (byte)v,
        EI2 v => (short)v,
        EU2 v => (ushort)v,
        EI4 v => (int)v,
        EU4 v => (uint)v,
        EI8 v => (long)v,
        EU8 v => unchecked((long)(ulong)v),
        _ => long.MinValue,
    };
}
