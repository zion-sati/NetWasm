using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EncodingProviderContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ProvidersPreserveLookupFallbackAndEnumerationContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ProviderFixture", Source, "ProviderFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Text;
        using System.Collections.Generic;
        namespace ProviderFixture;
        public class Provider : EncodingProvider
        {
            public Encoding Result;
            public int Enumerations;
            public bool OverrideBuiltins;
            public Provider(Encoding result) { Result = result; }
            public override Encoding GetEncoding(int codepage) =>
                codepage == 50001 || (OverrideBuiltins && (codepage == 0 || codepage == -42)) ? Result : null;
            public override Encoding GetEncoding(string name) =>
                name == "custom" || (OverrideBuiltins && name == "utf-8") ? Result : null;
            public override IEnumerable<EncodingInfo> GetEncodings()
            {
                Enumerations++;
                return new[] { new EncodingInfo(this, 50001, "custom", "Custom"), new EncodingInfo(this, 65001, "override", "Override") };
            }
        }
        public sealed class FallbackProvider : EncodingProvider
        {
            public override Encoding GetEncoding(int codepage) => null;
            public override Encoding GetEncoding(string name) => null;
            public override Encoding GetEncoding(int codepage, EncoderFallback encoder, DecoderFallback decoder) =>
                codepage == 50002 ? Encoding.UTF8 : null;
            public override Encoding GetEncoding(string name, EncoderFallback encoder, DecoderFallback decoder) =>
                name == "special" ? Encoding.UTF8 : null;
            public override IEnumerable<EncodingInfo> GetEncodings() => null;
        }
        public sealed class NormalizingEncoding : ASCIIEncoding
        {
            public override bool IsAlwaysNormalized(NormalizationForm form) => form == NormalizationForm.FormC;
        }
        public sealed class CustomEncoding : ASCIIEncoding
        {
            public static int Constructors;
            public readonly object State;
            public int Marker;
            public CustomEncoding(object state) { Constructors++; State = state; Marker = 123; }
            public override int CodePage => 50003;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                try { Encoding.RegisterProvider(null); return 1; }
                catch (ArgumentNullException e) { if (e.ParamName != "provider") return 2; }
                var first = new Provider(Encoding.ASCII);
                Encoding.RegisterProvider(first); Encoding.RegisterProvider(first);
                Encoding.RegisterProvider(new Provider(Encoding.UTF8));
                Encoding.RegisterProvider(new FallbackProvider());
                if (!ReferenceEquals(Encoding.GetEncoding(50001), Encoding.ASCII) || !ReferenceEquals(Encoding.GetEncoding("custom"), Encoding.ASCII)) return 3;
                var enc = new EncoderReplacementFallback("!");
                var dec = new DecoderReplacementFallback("~");
                var byId = Encoding.GetEncoding(50001, enc, dec);
                var byName = Encoding.GetEncoding("custom", enc, dec);
                if (ReferenceEquals(byId, Encoding.ASCII) || byId.IsReadOnly || byName.IsReadOnly) return 4;
                if (!ReferenceEquals(byId.EncoderFallback, enc) || !ReferenceEquals(byName.DecoderFallback, dec)) return 5;
                if (ReferenceEquals(Encoding.ASCII.EncoderFallback, enc)) return 6;
                if (!ReferenceEquals(Encoding.GetEncoding(50002, null, null), Encoding.UTF8)) return 7;
                if (!ReferenceEquals(Encoding.GetEncoding("special", null, null), Encoding.UTF8)) return 8;
                if (Encoding.GetEncoding("utf-8").CodePage != 65001) return 9;
                first.OverrideBuiltins = true;
                if (!ReferenceEquals(Encoding.GetEncoding("utf-8"), first.Result) || !ReferenceEquals(Encoding.GetEncoding(0), first.Result)) return 10;
                if (!ReferenceEquals(Encoding.GetEncoding(-42), first.Result)) return 11;
                if (Encoding.Default.CodePage != 65001) return 12;
                var customCount = 0; var builtinCount = 0;
                foreach (var info in Encoding.GetEncodings())
                {
                    if (info.CodePage == 50001) { customCount++; if (info.Name != "custom" || !ReferenceEquals(info.GetEncoding(), first.Result)) return 13; }
                    if (info.CodePage == 65001) { builtinCount++; if (info.Name != "override") return 14; }
                }
                if (customCount != 1 || builtinCount != 1 || first.Enumerations != 1) return 15;
                try { Encoding.GetEncoding(-3); return 16; } catch (ArgumentOutOfRangeException) { }
                try { Encoding.GetEncoding((string)null); return 17; } catch (ArgumentNullException) { }
                if ((int)NormalizationForm.FormC != 1 || (int)NormalizationForm.FormD != 2 || (int)NormalizationForm.FormKC != 5 || (int)NormalizationForm.FormKD != 6) return 18;
                if (Encoding.ASCII.IsAlwaysNormalized() || Encoding.UTF8.IsAlwaysNormalized((NormalizationForm)123)) return 19;
                Encoding normal = new NormalizingEncoding();
                if (!normal.IsAlwaysNormalized() || normal.IsAlwaysNormalized(NormalizationForm.FormD)) return 20;
                var state = new object();
                var custom = new CustomEncoding(state);
                var customCopy = (CustomEncoding)custom.Clone();
                if (CustomEncoding.Constructors != 1 || ReferenceEquals(custom, customCopy)) return 21;
                if (!ReferenceEquals(customCopy.State, state) || customCopy.Marker != 123 || customCopy.CodePage != 50003) return 22;
                if (customCopy.IsReadOnly || !custom.IsReadOnly) return 23;
                customCopy.Marker = 321;
                customCopy.EncoderFallback = enc;
                if (custom.Marker != 123 || ReferenceEquals(custom.EncoderFallback, enc)) return 24;
                first.Result = custom;
                var suppliedCopy = (CustomEncoding)Encoding.GetEncoding(50001, enc, dec);
                if (suppliedCopy.IsReadOnly || !ReferenceEquals(suppliedCopy.State, state) || suppliedCopy.Marker != 123) return 25;
                if (!ReferenceEquals(suppliedCopy.EncoderFallback, enc) || !ReferenceEquals(suppliedCopy.DecoderFallback, dec)) return 26;
                if (CustomEncoding.Constructors != 1) return 27;
                return input + 1;
            }
        }
        """;
}
