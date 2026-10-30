using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnumObjectArgumentCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ObjectArgumentsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeEnumFixture", DirectSource);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeEnumFixture.EntryPoint",
            "Run", [new("run", "TypeEnumFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private static string DirectSource => Source
        .Replace("type.GetEnumName(", "Enum.GetName(type, ")
        .Replace("type.IsEnumDefined(", "Enum.IsDefined(type, ")
        .Replace("type.GetEnumNames()", "Enum.GetNames(type)")
        .Replace("type.GetEnumValues()", "Enum.GetValues(type)")
        .Replace("type.GetEnumUnderlyingType()", "Enum.GetUnderlyingType(type)")
        .Replace("typeof(Wide).GetEnumUnderlyingType()", "Enum.GetUnderlyingType(typeof(Wide))")
        .Replace("typeof(Wide).GetEnumName(", "Enum.GetName(typeof(Wide), ")
        .Replace("typeof(Wide).IsEnumDefined(", "Enum.IsDefined(typeof(Wide), ")
        .Replace("typeof(int).GetEnumNames()", "Enum.GetNames(typeof(int))")
        .Replace("typeof(int).GetEnumValues()", "Enum.GetValues(typeof(int))")
        .Replace("typeof(int).GetEnumUnderlyingType()", "Enum.GetUnderlyingType(typeof(int))");

    private const string Source = """
        using System;
        namespace TypeEnumFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var type = typeof(Signed);
                if (type.GetEnumUnderlyingType() != typeof(int) || typeof(Wide).GetEnumUnderlyingType() != typeof(ulong)) return 1;
                if (type.GetEnumName(Signed.One) != "One") return 21;
                if (type.GetEnumName(1L) != "One") return 22;
                if (type.GetEnumName(9) != null) return 23;
                var names = type.GetEnumNames();
                if (names.Length != 3 || names[0] != "Zero" || names[1] != "One" || names[2] != "Negative") return 3;
                names[0] = "changed";
                if (type.GetEnumNames()[0] != "Zero") return 4;
                var values = (Signed[])type.GetEnumValues();
                if (values.Length != 3 || values[0] != Signed.Zero || values[1] != Signed.One || values[2] != Signed.Negative) return 5;
                values[0] = Signed.One;
                if (((Signed[])type.GetEnumValues())[0] != Signed.Zero) return 6;
                if (!type.IsEnumDefined("One")) return 71;
                if (!type.IsEnumDefined(Signed.Negative)) return 72;
                if (!type.IsEnumDefined(1)) return 73;
                if (type.IsEnumDefined(9)) return 74;
                if (type.IsEnumDefined("one")) return 75;
                if (typeof(Wide).GetEnumName(ulong.MaxValue) != "Max" || !typeof(Wide).IsEnumDefined(ulong.MaxValue)) return 8;
                object[] ones = { (sbyte)1, (byte)1, (short)1, (ushort)1, 1, 1u, 1L, 1UL, (char)1 };
                foreach (var one in ones)
                    if (Enum.GetName(typeof(Signed), one) != "One") return 81;
                if (Enum.GetName(typeof(Signed), -1L) != "Negative" || Enum.GetName(typeof(Signed), ulong.MaxValue) != "Negative") return 82;
                if (Enum.GetName(typeof(Signed), 0x100000001L) != null || Enum.GetName(typeof(Signed), uint.MaxValue) != null) return 83;
                if (Enum.GetName(typeof(Tiny), 257) != null || Enum.GetName(typeof(Tiny), -1) != null || Enum.GetName(typeof(Tiny), (short)1) != "One") return 84;
                if (Enum.GetName(typeof(Narrow), -1L) != "Negative" || Enum.GetName(typeof(Narrow), 255) != null) return 85;
                var caught = 0;
                try { type.GetEnumName(null); } catch (ArgumentNullException) { caught++; }
                try { type.IsEnumDefined(null); } catch (ArgumentNullException) { caught++; }
                try { type.GetEnumName("One"); } catch (ArgumentException) { caught++; }
                try { type.IsEnumDefined(1L); } catch (ArgumentException) { caught++; }
                try { type.IsEnumDefined(Wide.Zero); } catch (ArgumentException) { caught++; }
                try { typeof(int).GetEnumNames(); } catch (ArgumentException) { caught++; }
                try { typeof(int).GetEnumValues(); } catch (ArgumentException) { caught++; }
                try { typeof(int).GetEnumUnderlyingType(); } catch (ArgumentException) { caught++; }
                try { Enum.GetName(type, (nint)1); } catch (ArgumentException) { caught++; }
                try { Enum.GetName(type, (nuint)1); } catch (ArgumentException) { caught++; }
                return caught == 10 ? input + 1 : 9;
            }
            private enum Tiny : byte { One = 1 }
            private enum Narrow : sbyte { Negative = -1 }
            private enum Signed { Negative = -1, Zero = 0, One = 1 }
            private enum Wide : ulong { Zero = 0, Max = ulong.MaxValue }
        }
        """;
}
