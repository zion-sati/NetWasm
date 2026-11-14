using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ValueTypeConverterCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ValueConvertersPreserveInvariantParsingAndFormatting(WasmTarget target)
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
                TypeConverter[] converters = { new GuidConverter(), new TimeSpanConverter(), new DateTimeConverter(), new DateTimeOffsetConverter() };
                string[] texts = { "12345678-1234-5678-90ab-1234567890ab", "1.02:03:04", "2026-01-02", "2026-01-02 00:00:00 +02:00" };
                object[] expected = { new Guid(texts[0]), new TimeSpan(1, 2, 3, 4), new DateTime(2026, 1, 2), new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(2)) };
                string[] formatted = { texts[0], texts[1], texts[2], "2026-01-02 +02:00" };
                var caught = 0;
                for (var i = 0; i < converters.Length; i++)
                {
                    var converter = converters[i];
                    if (!converter.CanConvertFrom(typeof(string)) || !converter.CanConvertTo(typeof(InstanceDescriptor)) || !converter.CanConvertTo(typeof(string))) return 10 + i;
                    if (!converter.ConvertFromInvariantString(texts[i]).Equals(expected[i])) return 20 + i;
                    if (converter.ConvertToInvariantString(expected[i]) != formatted[i]) return 20 + i;
                    if (converter.IsValid("not-valid")) return 30 + i;
                    try { converter.ConvertFromString("not-valid"); } catch (FormatException e) { if (!string.IsNullOrEmpty(e.Message)) caught++; }
                    try { converter.ConvertTo(expected[i], null); } catch (ArgumentNullException) { caught++; }
                    try { converter.ConvertTo(expected[i], typeof(InstanceDescriptor)); } catch (PlatformNotSupportedException) { caught++; }
                    try { converter.ConvertFrom(42); } catch (NotSupportedException) { caught++; }
                }
                if (!converters[2].ConvertFromString("  ").Equals(DateTime.MinValue) || converters[2].ConvertToInvariantString(DateTime.MinValue) != "") return 1;
                if (!converters[3].ConvertFromString("  ").Equals(DateTimeOffset.MinValue) || converters[3].ConvertToInvariantString(DateTimeOffset.MinValue) != "") return 2;
                if (converters[2].ConvertToInvariantString(new DateTime(2026, 1, 2, 3, 4, 5)) != "01/02/2026 03:04:05") return 3;
                if (converters[3].ConvertToInvariantString(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-3))) != "01/02/2026 03:04:05 -03:00") return 4;
                return caught == 16 ? input + 1 : 100 + caught;
            }
        }
        """;
}
