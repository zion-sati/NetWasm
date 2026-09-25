using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class IConvertibleTypeCodeCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesIConvertibleAndTypeCodeContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assemblyName = $"IConvertibleTypeCodeFixture_{optimize}_{target}";
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            assemblyName,
            Source,
            "IConvertibleTypeCodeFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, observed);
    }

    private const string Source = """
        using System;

        namespace IConvertibleTypeCodeFixture;

        public enum Sample : int
        {
            One = 1,
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if ((int)TypeCode.Empty != 0 ||
                    (int)TypeCode.Object != 1 ||
                    (int)TypeCode.DBNull != 2 ||
                    (int)TypeCode.Boolean != 3 ||
                    (int)TypeCode.Char != 4 ||
                    (int)TypeCode.SByte != 5 ||
                    (int)TypeCode.Byte != 6 ||
                    (int)TypeCode.Int16 != 7 ||
                    (int)TypeCode.UInt16 != 8 ||
                    (int)TypeCode.Int32 != 9 ||
                    (int)TypeCode.UInt32 != 10 ||
                    (int)TypeCode.Int64 != 11 ||
                    (int)TypeCode.UInt64 != 12 ||
                    (int)TypeCode.Single != 13 ||
                    (int)TypeCode.Double != 14 ||
                    (int)TypeCode.Decimal != 15 ||
                    (int)TypeCode.DateTime != 16 ||
                    (int)TypeCode.String != 18)
                {
                    return 1;
                }

                IConvertible value = Sample.One;
                if (value.GetTypeCode() != TypeCode.Int32) return 2;
                if (!value.ToBoolean(null)) return 3;
                if (value.ToChar(null) != (char)1) return 4;
                if (value.ToSByte(null) != 1) return 5;
                if (value.ToByte(null) != 1) return 6;
                if (value.ToInt16(null) != 1) return 7;
                if (value.ToUInt16(null) != 1) return 8;
                if (value.ToInt32(null) != 1) return 9;
                if (value.ToUInt32(null) != 1) return 10;
                if (value.ToInt64(null) != 1) return 11;
                if (value.ToUInt64(null) != 1) return 12;
                if (value.ToSingle(null) != 1) return 13;
                if (value.ToDouble(null) != 1) return 14;
                if (value.ToDecimal(null) != 1m) return 15;
                if (value.ToString(null) != "One") return 16;
                if ((string)value.ToType(typeof(string), null) != "One") return 17;

                try
                {
                    _ = value.ToDateTime(null);
                    return 18;
                }
                catch (InvalidCastException)
                {
                }

                return input - input;
            }
        }
        """;
}
