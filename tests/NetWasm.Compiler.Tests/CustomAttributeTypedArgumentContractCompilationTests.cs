using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CustomAttributeTypedArgumentContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypedAttributeValuesPreserveUpstreamSemantics(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypedAttributeFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypedAttributeFixture.EntryPoint",
            "Run", [new("run", "TypedAttributeFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            // The checks read type names, which a program carries only in its type-symbols sidecar (issue 225).
            EmitTypeNames: true));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd,
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, timeZoneAssetPath: null,
            typeSymbols: result.TypeSymbols!.Bytes));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace TypedAttributeFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                var values = new[] { new CustomAttributeTypedArgument(1) };
                var array = new CustomAttributeTypedArgument(typeof(int[]), values);
                if (!ReferenceEquals(array.Value, values) || array.ArgumentType != typeof(int[])) return 21;
                try { array.ToString(); return 22; } catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 23; }
                return input + 1;
            }
            public static int Portable(int input)
            {
                object boxed = 42;
                var first = new CustomAttributeTypedArgument(boxed);
                var same = new CustomAttributeTypedArgument(typeof(int), boxed);
                if (first.ArgumentType != typeof(int) || !ReferenceEquals(first.Value, boxed) || first.ToString() != "(Int32)42") return 1;
                if (first != same || !first.Equals((object)same) || first.Equals(null) || first.Equals(new object())) return 2;
                var separate = new CustomAttributeTypedArgument(typeof(int), (object)42);
                if (first == separate) return 3;
                if (first.GetHashCode() != same.GetHashCode()) return 4;
                var text = new CustomAttributeTypedArgument("abc");
                if (text.ToString() != "\"abc\"") return 5;
                if (new CustomAttributeTypedArgument('x').ToString() != "'x'") return 6;
                if (new CustomAttributeTypedArgument(typeof(string), null).ToString() != "(String)null") return 7;
                if (new CustomAttributeTypedArgument(typeof(Type), typeof(int)).ToString() != "typeof(System.Int32)") return 8;
                var number = new CustomAttributeTypedArgument(Signed.Negative);
                if (number.ArgumentType != typeof(Signed) || number.Value is not short || (short)number.Value != -3) return 9;
                if (!number.ToString().EndsWith("-3")) return 10;
                var large = new CustomAttributeTypedArgument(Unsigned.Max);
                if (large.Value is not ulong || (ulong)large.Value != ulong.MaxValue) return 11;
                try { new CustomAttributeTypedArgument((object)null!); return 12; } catch (ArgumentNullException e) { if (e.ParamName != "value") return 13; }
                try { new CustomAttributeTypedArgument(null!, 1); return 14; } catch (ArgumentNullException e) { if (e.ParamName != "argumentType") return 15; }
                return input + 1;
            }
            private enum Signed : short { Negative = -3 }
            private enum Unsigned : ulong { Max = ulong.MaxValue }
        }
        """;
}
