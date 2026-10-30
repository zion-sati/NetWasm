using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConvertChangeTypeCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypeDestinationsPreserveConvertibleDispatch(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CategoryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CategoryFixture.EntryPoint",
            "Run", [new("run", "CategoryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        namespace CategoryFixture;
        public sealed class Target { }
        public sealed class Provider : IFormatProvider { public object GetFormat(Type type) => null; }
        public sealed class Custom : IConvertible
        {
            public IFormatProvider Seen;
            public Type Destination;
            public TypeCode GetTypeCode() => TypeCode.Object;
            public object ToType(Type type, IFormatProvider provider) { Destination = type; Seen = provider; return new Target(); }
            public int ToInt32(IFormatProvider provider) { Seen = provider; return 73; }
            public bool ToBoolean(IFormatProvider provider) => throw new NotSupportedException();
            public char ToChar(IFormatProvider provider) => throw new NotSupportedException();
            public sbyte ToSByte(IFormatProvider provider) => throw new NotSupportedException();
            public byte ToByte(IFormatProvider provider) => throw new NotSupportedException();
            public short ToInt16(IFormatProvider provider) => throw new NotSupportedException();
            public ushort ToUInt16(IFormatProvider provider) => throw new NotSupportedException();
            public uint ToUInt32(IFormatProvider provider) => throw new NotSupportedException();
            public long ToInt64(IFormatProvider provider) => throw new NotSupportedException();
            public ulong ToUInt64(IFormatProvider provider) => throw new NotSupportedException();
            public float ToSingle(IFormatProvider provider) => throw new NotSupportedException();
            public double ToDouble(IFormatProvider provider) => throw new NotSupportedException();
            public decimal ToDecimal(IFormatProvider provider) => throw new NotSupportedException();
            public DateTime ToDateTime(IFormatProvider provider) => throw new NotSupportedException();
            public string ToString(IFormatProvider provider) => throw new NotSupportedException();
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Type[] types = { typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) };
                object[] expected = { (sbyte)42, (byte)42, (short)42, (ushort)42, 42, 42u, 42L, 42ul, 42f, 42d, 42m };
                for (var i = 0; i < types.Length; i++)
                    if (!Convert.ChangeType("42", types[i], CultureInfo.InvariantCulture).Equals(expected[i])) return 10 + i;
                if (!(bool)Convert.ChangeType("true", typeof(bool)) || (char)Convert.ChangeType("x", typeof(char)) != 'x' || (string)Convert.ChangeType(42, typeof(string)) != "42") return 1;
                var now = new DateTime(2026, 1, 2); if (!Convert.ChangeType(now, typeof(DateTime)).Equals(now)) return 2;
                var value = new Target(); if (!ReferenceEquals(Convert.ChangeType(value, typeof(Target)), value)) return 3;
                object boxed = 42; if (!ReferenceEquals(Convert.ChangeType(boxed, typeof(object)), boxed)) return 4;
                if (Convert.ChangeType(null, typeof(string)) != null || Convert.ChangeType(null, typeof(Target)) != null) return 5;
                var custom = new Custom(); var provider = new Provider();
                if ((int)Convert.ChangeType(custom, typeof(int), provider) != 73 || custom.Seen != provider) return 6;
                if (!(Convert.ChangeType(custom, typeof(Target), provider) is Target) || custom.Seen != provider || custom.Destination != typeof(Target)) return 7;
                custom.Seen = null; if ((int)Convert.ChangeType(custom, typeof(int)) != 73 || custom.Seen != CultureInfo.CurrentCulture) return 8;
                var caught = 0;
                try { Convert.ChangeType(null, (Type)null); } catch (ArgumentNullException e) { if (e.ParamName == "conversionType") caught++; }
                try { Convert.ChangeType(null, typeof(int)); } catch (InvalidCastException) { caught++; }
                try { Convert.ChangeType(null, typeof(int?)); } catch (InvalidCastException) { caught++; }
                try { Convert.ChangeType(value, typeof(object)); } catch (InvalidCastException) { caught++; }
                try { Convert.ChangeType(256, typeof(byte)); } catch (OverflowException) { caught++; }
                return caught == 5 ? input + 1 : 100 + caught;
            }
        }
        """;
}
