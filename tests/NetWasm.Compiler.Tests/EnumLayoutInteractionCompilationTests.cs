using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumLayoutInteractionCompilationTests
{
    public static TheoryData<bool, WasmTarget> TargetProfiles =>
    new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [InlineData(0, false, WasmTarget.Wasm32)]
    [InlineData(0, false, WasmTarget.Wasm64)]
    [InlineData(0, true, WasmTarget.Wasm32)]
    [InlineData(0, true, WasmTarget.Wasm64)]
    [InlineData(1, false, WasmTarget.Wasm32)]
    [InlineData(1, false, WasmTarget.Wasm64)]
    [InlineData(1, true, WasmTarget.Wasm32)]
    [InlineData(1, true, WasmTarget.Wasm64)]
    [InlineData(2, false, WasmTarget.Wasm32)]
    [InlineData(2, false, WasmTarget.Wasm64)]
    [InlineData(2, true, WasmTarget.Wasm32)]
    [InlineData(2, true, WasmTarget.Wasm64)]
    [InlineData(3, false, WasmTarget.Wasm32)]
    [InlineData(3, false, WasmTarget.Wasm64)]
    [InlineData(3, true, WasmTarget.Wasm32)]
    [InlineData(3, true, WasmTarget.Wasm64)]
    public void CompilerExecutesEnumLayoutInteractionsAcrossTargets(
        int family,
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "EnumLayoutInteractionFixture",
            CreateSource(family),
            "EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [MemberData(nameof(TargetProfiles))]
    public void CompilerExecutesEnumGetNameAfterTypeBasedParse(bool optimize, WasmTarget target) =>
        ExecuteMetadataOperation(0, optimize, target);

    [Theory]
    [MemberData(nameof(TargetProfiles))]
    public void CompilerExecutesEnumGetNamesAfterTypeBasedParse(bool optimize, WasmTarget target) =>
        ExecuteMetadataOperation(1, optimize, target);

    [Theory]
    [MemberData(nameof(TargetProfiles))]
    public void CompilerExecutesEnumGetValuesAfterTypeBasedParse(bool optimize, WasmTarget target) =>
        ExecuteMetadataOperation(2, optimize, target);

    [Theory]
    [MemberData(nameof(TargetProfiles))]
    public void CompilerExecutesEnumGetUnderlyingTypeAfterTypeBasedParse(bool optimize, WasmTarget target) =>
        ExecuteMetadataOperation(3, optimize, target);

    [Theory]
    [MemberData(nameof(TargetProfiles))]
    public void CompilerExecutesEnumIsDefinedAfterTypeBasedParse(bool optimize, WasmTarget target) =>
        ExecuteMetadataOperation(4, optimize, target);

    [Theory]
    [MemberData(nameof(TargetProfiles))]
    public void CompilerExecutesEnumToObjectAfterTypeBasedParse(bool optimize, WasmTarget target) =>
        ExecuteMetadataOperation(5, optimize, target);

    private static string CreateSource(int family)
    {
        var assertion = family switch
        {
            0 => "return ((IConvertible)E.A).GetTypeCode() == TypeCode.Int32 && E.A.ToString() == \"A\" ? 0 : 2;",
            1 => "return Enum.GetName(typeof(E), E.A) == \"A\" && Enum.GetNames<E>().Length == 2 && Enum.GetValues<E>().Length == 2 && Enum.GetUnderlyingType(typeof(E)) == typeof(int) && Enum.IsDefined(E.A) && (E)Enum.ToObject(typeof(E), 1) == E.A ? 0 : 3;",
            2 => "return Enum.Format(typeof(E), 42, \"D\") == \"42\" && Enum.Parse<E>(\"A\") == E.A && Enum.TryParse<E>(\"B\", out var value) && value == E.B ? 0 : 4;",
            3 => "try { _ = Enum.GetName(null!, E.A); return 5; } catch (ArgumentNullException) { return 0; }",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

        return $$"""
            using System;

            public enum E : int
            {
                A = 1,
                B = 2
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if ((E)Enum.Parse(typeof(E), "42") != (E)42)
                    {
                        return 1;
                    }

                    {{assertion}}
                }
            }
            """;
    }

    private static void ExecuteMetadataOperation(int operation, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "EnumMetadataLayoutInteractionFixture",
            CreateMetadataSource(operation),
            "EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    private static string CreateMetadataSource(int operation)
    {
        var assertion = operation switch
        {
            0 => "return Enum.GetName(typeof(E), E.A) == \"A\" ? 0 : 2;",
            1 => "return Enum.GetNames<E>().Length == 2 ? 0 : 3;",
            2 => "return Enum.GetValues<E>().Length == 2 ? 0 : 4;",
            3 => "return Enum.GetUnderlyingType(typeof(E)) == typeof(int) ? 0 : 5;",
            4 => "return Enum.IsDefined(E.A) ? 0 : 6;",
            5 => "return (E)Enum.ToObject(typeof(E), 1) == E.A ? 0 : 7;",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        return $$"""
            using System;

            public enum E : int
            {
                A = 1,
                B = 2
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if ((E)Enum.Parse(typeof(E), "42") != (E)42)
                    {
                        return 1;
                    }

                    {{assertion}}
                }
            }
            """;
    }
}
