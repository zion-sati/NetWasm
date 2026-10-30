using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumToTypeCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BoxedEnumsConvertToPrimitiveAndIdentityDestinations(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnumConversionFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EnumConversionFixture.EntryPoint",
            "Run", [new("run", "EnumConversionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace EnumConversionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object boxed = Signed.Negative;
                var value = (IConvertible)boxed;
                if ((short)value.ToType(typeof(short), null) != -3) return 1;
                if ((int)value.ToType(typeof(int), null) != -3) return 2;
                if ((long)value.ToType(typeof(long), null) != -3) return 3;
                if ((float)value.ToType(typeof(float), null) != -3) return 4;
                if ((double)value.ToType(typeof(double), null) != -3) return 5;
                if ((decimal)value.ToType(typeof(decimal), null) != -3) return 6;
                if (!(bool)value.ToType(typeof(bool), null)) return 7;
                if ((string)value.ToType(typeof(string), null) != "Negative") return 8;
                if (!ReferenceEquals(boxed, value.ToType(typeof(object), null))) return 9;
                if (!ReferenceEquals(boxed, value.ToType(typeof(Enum), null))) return 10;
                if (!ReferenceEquals(boxed, value.ToType(typeof(Signed), null))) return 11;
                var large = (IConvertible)Unsigned.Max;
                if ((ulong)large.ToType(typeof(ulong), null) != ulong.MaxValue) return 12;
                try { value.ToType(typeof(uint), null); return 13; } catch (OverflowException) { }
                try { large.ToType(typeof(long), null); return 14; } catch (OverflowException) { }
                try { value.ToType(typeof(DateTime), null); return 15; } catch (InvalidCastException) { }
                try { value.ToType(typeof(Unsigned), null); return 16; } catch (InvalidCastException) { }
                try { value.ToType(null!, null); return 17; } catch (ArgumentNullException) { }
                var positive = (IConvertible)Unsigned.Small;
                if ((byte)positive.ToType(typeof(byte), null) != 65) return 18;
                if ((sbyte)positive.ToType(typeof(sbyte), null) != 65) return 19;
                if ((ushort)positive.ToType(typeof(ushort), null) != 65) return 20;
                if ((uint)positive.ToType(typeof(uint), null) != 65) return 21;
                if ((char)positive.ToType(typeof(char), null) != 'A') return 22;
                return input + 1;
            }
            private enum Signed : short { Negative = -3 }
            private enum Unsigned : ulong { Small = 65, Max = ulong.MaxValue }
        }
        """;
}
