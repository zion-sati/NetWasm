using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Every enum type reaches System.Enum's IConvertible members, so an IConvertible call site dispatches over all of a
// program's enum types (Newtonsoft.Json's program has hundreds, from System.Xml and System.Data). Receivers that
// share a target share one emitted body; one body per receiver, each switching over the enum types again, made
// Convert.ChangeType grow with the square of the enum count (53 MB for 300 enums, over the engines' function limit).
public sealed class EnumConvertibleDispatchCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EnumsOfEveryUnderlyingTypeConvertThroughIConvertible(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var result = Compile(assets, 48, target);

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void DispatchGrowsLinearlyWithTheEnumTypes()
    {
        using var assets = TestAssets.Create();
        var none = Compile(assets, 0, WasmTarget.Wasm32).ApplicationModule.Length;
        var half = Compile(assets, 32, WasmTarget.Wasm32).ApplicationModule.Length;
        var full = Compile(assets, 64, WasmTarget.Wasm32).ApplicationModule.Length;

        // Linear growth adds as much for the second 32 enums as for the first; quadratic adds three times as much.
        Assert.True(full - half < 2 * (half - none),
            $"0 enums: {none} bytes, 32: {half}, 64: {full}");
    }

    private static CompilationResult Compile(TestAssets assets, int enumCount, WasmTarget target)
    {
        var name = $"EnumConvertibleFixture{enumCount}";
        var assembly = assets.CompileOptimizedSource(name, Source(name, enumCount));
        return NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], $"{name}.EntryPoint", "Run",
            [new("run", $"{name}.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
    }

    private static readonly string[] Underlying = ["byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong"];
    private static readonly string[] TypeCodes = ["Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64"];

    // Enum E<i> has the underlying type Underlying[i % 8] and its member B<i> the value i + 1.
    private static string Source(string name, int enumCount)
    {
        var source = new StringBuilder($$"""
            using System;
            using System.Globalization;
            namespace {{name}};

            public static class EntryPoint
            {
                private static readonly object[] Values = {
            """);
        for (var index = 0; index < enumCount; index++) source.Append(CultureInfo.InvariantCulture, $"E{index}.B{index}, ");
        source.Append("""
             };
                private static readonly TypeCode[] Codes = {
            """);
        for (var index = 0; index < enumCount; index++) source.Append(CultureInfo.InvariantCulture, $"TypeCode.{TypeCodes[index % 8]}, ");
        source.Append("""
             };

                public static int Run(int input)
                {
                    for (int i = 0; i < Values.Length; i++)
                    {
                        object value = Values[i];
                        var convertible = (IConvertible)value;
                        if ((long)Convert.ChangeType(value, typeof(long), CultureInfo.InvariantCulture) != i + 1) return 1;
                        if ((decimal)Convert.ChangeType(value, typeof(decimal), CultureInfo.InvariantCulture) != i + 1) return 2;
                        if ((string)Convert.ChangeType(value, typeof(string), CultureInfo.InvariantCulture) != "B" + i) return 3;
                        if (convertible.ToInt32(CultureInfo.InvariantCulture) != i + 1) return 4;
                        if (convertible.ToDouble(CultureInfo.InvariantCulture) != i + 1) return 5;
                        if (convertible.GetTypeCode() != Codes[i]) return 6;
                        if (convertible.ToString(CultureInfo.InvariantCulture) != "B" + i) return 7;
                        try { convertible.ToDateTime(CultureInfo.InvariantCulture); return 8; } catch (InvalidCastException) { }
                    }

                    object number = input;
                    if ((long)Convert.ChangeType(number, typeof(long), CultureInfo.InvariantCulture) != input) return 9;
                    return input + 1;
                }
            }

            """);
        for (var index = 0; index < enumCount; index++)
            source.Append(CultureInfo.InvariantCulture, $"enum E{index} : {Underlying[index % 8]} {{ A{index}, B{index} = {index + 1} }}\n");
        return source.ToString();
    }
}
