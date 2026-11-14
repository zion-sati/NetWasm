using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class UriTypeConverterCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UriConversionsPreserveOriginalTextAndBoundaries(WasmTarget target)
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
        using System.ComponentModel.Design.Serialization;
        namespace CategoryFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var converter = new UriTypeConverter();
                if (!converter.CanConvertFrom(typeof(string)) || !converter.CanConvertFrom(typeof(Uri)) || converter.CanConvertFrom(typeof(int))) return 1;
                if (!converter.CanConvertTo(typeof(string)) || !converter.CanConvertTo(typeof(Uri)) || !converter.CanConvertTo(typeof(InstanceDescriptor)) || converter.CanConvertTo(typeof(int))) return 2;
                if (converter.ConvertFromString("") != null) return 3;
                string[] values = { "https://example.org/a%20b?q=1#part", "../relative?q=x", "urn:example:test" };
                for (var i = 0; i < values.Length; i++)
                {
                    var uri = (Uri)converter.ConvertFromString(values[i]);
                    var copy = (Uri)converter.ConvertFrom(uri);
                    var converted = (Uri)converter.ConvertTo(uri, typeof(Uri));
                    if (uri.OriginalString != values[i] || copy.OriginalString != values[i] || converted.OriginalString != values[i]) return 10 + i;
                    if (ReferenceEquals(uri, copy) || ReferenceEquals(uri, converted) || uri.IsAbsoluteUri != copy.IsAbsoluteUri) return 20 + i;
                    if (converter.ConvertToString(uri) != values[i] || !converter.IsValid(values[i]) || !converter.IsValid(uri)) return 30 + i;
                }
                if (converter.IsValid(null) || converter.IsValid(42) || converter.IsValid("http://[")) return 4;
                var caught = 0;
                try { converter.ConvertFrom(42); } catch (NotSupportedException) { caught++; }
                try { converter.ConvertTo(42, typeof(string)); } catch (NotSupportedException) { caught++; }
                try { converter.ConvertTo(new Uri("https://example.org"), null); } catch (ArgumentNullException) { caught++; }
                try { converter.ConvertFromString("http://["); } catch (UriFormatException) { caught++; }
                try { converter.ConvertTo(new Uri("https://example.org"), typeof(InstanceDescriptor)); } catch (PlatformNotSupportedException) { caught++; }
                return caught == 5 ? input + 1 : 100 + caught;
            }
        }
        """;
}
