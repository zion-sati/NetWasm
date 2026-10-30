using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DateTimeConvertibleCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DateTimePreservesConvertibleIdentityAndRejectsNumericConversions(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DateTimeConvertible", """
            using System;
            namespace Fixture;
            public static class Entry
            {
                public static int Run(int input)
                {
                    var date = new DateTime(2024, 2, 29, 12, 30, 45, DateTimeKind.Utc);
                    IConvertible value = date;
                    if (value.GetTypeCode() != TypeCode.DateTime || value.ToDateTime(null).Ticks != date.Ticks ||
                        value.ToDateTime(null).Kind != DateTimeKind.Utc) return 1;
                    if (((DateTime)value.ToType(typeof(DateTime), null)).Ticks != date.Ticks ||
                        ((DateTime)value.ToType(typeof(object), null)).Kind != DateTimeKind.Utc) return 2;
                    if ((string)value.ToType(typeof(string), null) != date.ToString((IFormatProvider?)null) ||
                        value.ToString(null) != date.ToString((IFormatProvider?)null)) return 3;
                    if (((DateTime)Convert.ChangeType(date, TypeCode.DateTime)!).Ticks != date.Ticks) return 4;
                    var rejected = 0;
                    for (var index = 0; index < 16; index++)
                    {
                        try
                        {
                            switch (index)
                            {
                                case 0: value.ToBoolean(null); break;
                                case 1: value.ToChar(null); break;
                                case 2: value.ToSByte(null); break;
                                case 3: value.ToByte(null); break;
                                case 4: value.ToInt16(null); break;
                                case 5: value.ToUInt16(null); break;
                                case 6: value.ToInt32(null); break;
                                case 7: value.ToUInt32(null); break;
                                case 8: value.ToInt64(null); break;
                                case 9: value.ToUInt64(null); break;
                                case 10: value.ToSingle(null); break;
                                case 11: value.ToDouble(null); break;
                                case 12: value.ToDecimal(null); break;
                                case 13: value.ToType(typeof(int), null); break;
                                case 14: value.ToType(typeof(IConvertible), null); break;
                                case 15: Convert.ChangeType(null, TypeCode.DateTime); break;
                            }
                        }
                        catch (InvalidCastException) { rejected++; }
                    }
                    try { value.ToType(null!, null); }
                    catch (ArgumentNullException) { rejected++; }
                    return rejected == 17 ? 42 : -rejected;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "Fixture.Entry", "Run",
            [new RequestedExport("run", "Fixture.Entry", "Run")])
        {
            Target = target,
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0"
        });
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }
}
