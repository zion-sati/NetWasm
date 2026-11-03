using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Enum.Parse<TEnum>, TryParse<TEnum> and TryFormat<TEnum> constrain TEnum only
// to a struct, so it may name no enum, as a Type passed to Enum's other
// members may. Each throws what desktop .NET 11 throws: ArgumentException
// "Type provided must be an Enum." for parameter TEnum or enumType. Each case
// sets its bit when it does; an enum still parses.
public sealed class EnumNonEnumTypeCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            private const string MustBeEnum = "Type provided must be an Enum.";

            private static int Bit(int index, string parameter, Action action)
            {
                try
                {
                    action();
                }
                catch (ArgumentException exception) when (
                    exception.GetType() == typeof(ArgumentException) && exception.ParamName == parameter &&
                    exception.Message == $"{MustBeEnum} (Parameter '{parameter}')")
                {
                    return 1 << index;
                }
                catch (Exception)
                {
                }
                return 0;
            }

            public static int Run(int input)
            {
                var text = input.ToString();
                var bits = 0;
                bits |= Bit(0, "enumType", () => Enum.TryFormat<int>(input, new char[8], out _, default));
                bits |= Bit(1, "TEnum", () => Enum.Parse<int>(text));
                bits |= Bit(2, "TEnum", () => Enum.TryParse<int>(text, out _));
                bits |= Bit(3, "TEnum", () => Enum.Parse<DateTime>(text.AsSpan(), true));
                bits |= Bit(4, "TEnum", () => Enum.TryParse<DateTime>(text, true, out _));
                bits |= Bit(5, "enumType", () => Enum.Parse(typeof(int), text));
                bits |= Bit(6, "enumType", () => Enum.TryParse(typeof(int), text, out _));
                bits |= Bit(7, "enumType", () => Enum.ToObject(typeof(int), input));
                bits |= Bit(8, "enumType", () => Enum.GetNames(typeof(int)));
                bits |= Bit(9, "enumType", () => Enum.GetValues(typeof(int)));
                bits |= Bit(10, "enumType", () => Enum.GetUnderlyingType(typeof(int)));
                bits |= Bit(11, "enumType", () => Enum.IsDefined(typeof(int), input));
                bits |= Bit(12, "enumType", () => Enum.GetName(typeof(int), input));
                bits |= Bit(13, "enumType", () => Enum.Format(typeof(int), input, "G"));
                return Enum.Parse<DayOfWeek>("Friday") == DayOfWeek.Friday ? bits : -1;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void EnumMembersRejectATypeThatIsNotAnEnum(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("EnumNonEnumType" + optimization, Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));
        Assert.Equal((1 << 14) - 1, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 5, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
