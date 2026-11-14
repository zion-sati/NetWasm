using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NumericTypeConverterCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NumericConvertersPreserveParsingFormattingAndFailures(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CategoryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CategoryFixture.EntryPoint",
            "Run", [new("run", "CategoryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.ComponentModel;
        using System.ComponentModel.Design.Serialization;
        namespace CategoryFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                TypeConverter[] converters = { new ByteConverter(), new SByteConverter(), new Int16Converter(), new UInt16Converter(), new Int32Converter(), new UInt32Converter(), new Int64Converter(), new UInt64Converter(), new SingleConverter(), new DoubleConverter(), new DecimalConverter() };
                object[] expected = { (byte)42, (sbyte)42, (short)42, (ushort)42, 42, 42u, 42L, 42ul, 42f, 42d, 42m };
                for (var i = 0; i < converters.Length; i++)
                {
                    var converter = converters[i];
                    if (!converter.CanConvertFrom(typeof(string)) || !converter.CanConvertTo(typeof(int)) || !converter.CanConvertTo(typeof(string))) return 10 + i;
                    if (!converter.ConvertFromInvariantString(" 42 ").Equals(expected[i]) || converter.ConvertToInvariantString(expected[i]) != "42") return 30 + i;
                    if ((int)converter.ConvertTo(expected[i], typeof(int)) != 42 || converter.IsValid("not-a-number")) return 50 + i;
                    if (i < 8 && (!converter.ConvertFromString("#2A").Equals(expected[i]) || !converter.ConvertFromString("0x2a").Equals(expected[i]) || !converter.ConvertFromString("&H2A").Equals(expected[i]))) return 70 + i;
                    var caught = 0;
                    try { converter.ConvertFromString("not-a-number"); } catch (ArgumentException e) { if (e.ParamName == "value" && e.InnerException != null && !string.IsNullOrEmpty(e.Message)) caught++; }
                    try { converter.ConvertFromString(""); } catch (ArgumentException e) { if (e.ParamName == "value") caught++; }
                    try { converter.ConvertTo(expected[i], null); } catch (ArgumentNullException) { caught++; }
                    if (caught != 3) return 90 + i;
                }
                if ((int)new Int32Converter().ConvertFromString("0xffffffff") != -1 || (ulong)new UInt64Converter().ConvertFromString("18446744073709551615") != ulong.MaxValue) return 1;
                if ((long)new Int64Converter().ConvertFromString("-9223372036854775808") != long.MinValue) return 2;
                if ((double)new DoubleConverter().ConvertFromString("1.25e2") != 125d || (float)new SingleConverter().ConvertFromString("1.25e2") != 125f || (decimal)new DecimalConverter().ConvertFromString("1.25e2") != 125m) return 3;
                var failures = 0;
                try { new ByteConverter().ConvertFromString("256"); } catch (ArgumentException e) { if (e.InnerException is OverflowException) failures++; }
                try { new UInt64Converter().ConvertFromString("-1"); } catch (ArgumentException e) { if (e.InnerException is OverflowException) failures++; }
                try { new Int32Converter().ConvertTo(256, typeof(byte)); } catch (OverflowException) { failures++; }
                try { new DecimalConverter().ConvertTo(42m, typeof(InstanceDescriptor)); } catch (PlatformNotSupportedException) { failures++; }
                return failures == 4 ? input + 1 : 120 + failures;
            }
        }
        """;
}
