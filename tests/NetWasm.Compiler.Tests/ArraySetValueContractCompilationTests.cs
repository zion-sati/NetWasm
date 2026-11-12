using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ArraySetValueContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AssignmentsMatchDesktopConversionAndExceptionContracts(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "AssignmentFixture", Source, "AssignmentFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        namespace AssignmentFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
        var sources = new (string Name, object? Value)[] {
         ("null",null),("bool",true),("sbyte",(sbyte)1),("byte",(byte)1),
         ("short",(short)1),("ushort",(ushort)1),("char",'A'),("int",1),("uint",1U),
         ("long",1L),("ulong",1UL),("float",1f),("double",1d),("decimal",1m),
         ("nint",(nint)1),("nuint",(nuint)1),("enum",Sample.One),("other-enum",Other.One),
         ("string","x"),("object",new object()),("struct",new Record { Number = 7 }),
         ("nullable-int",(int?)1),("nullable-enum",(Sample?)Sample.One)
        };
        var targets = new (string Name, Array Value)[] {
         ("bool",new bool[1]),("sbyte",new sbyte[1]),("byte",new byte[1]),
         ("short",new short[1]),("ushort",new ushort[1]),("char",new char[1]),
         ("int",new int[1]),("uint",new uint[1]),("long",new long[1]),("ulong",new ulong[1]),
         ("float",new float[1]),("double",new double[1]),("decimal",new decimal[1]),
         ("nint",new nint[1]),("nuint",new nuint[1]),("enum",new Sample[1]),
         ("object",new object[1]),("string",new string[1]),("struct",new Record[1]),
         ("nullable-int",new int?[1]),("nullable-enum",new Sample?[1]),
         ("nullable-struct",new Record?[1]),("icomparable",new IComparable[1])
        };
        const string expected = "SSSSSSSSSSSSSSSSSSSSSSSSAAAAAAAAAAAIAAISIIIIISASASAASASASSIAAISIIIIISAASSSSSSSSSSIAAISIIIIISAAASAASASASSIAAISIIIIISAAAASSSSSSSSIAAISIIIIISAAAASSSSSSSSIAAISIIIIISAAAAAASASASSIAAISIISIISAAAAAAASSSSSIAAISIIIIISAAAAAAAASASSIAAISIIIIISAAAAAAAAASSSIAAISIIIIISAAAAAAAAAASSIAAISIIIIISAAAAAAAAAAASIAAISIIIIISIIIIIIIIIIIISIIISIIIIISAAAAAAAAAAAAISAISIIIIISAAAAAAAAAAAAIASISIIIIISAAAAAASASASSIAASSIIISISAAAAAASASASSIAAISIIIIISIIIIIIIIIIIIIIIISSIIIISIIIIIIIIIIIIIIIISIIIIIIIIIIIIIIIIIIIIIISISIISIAAAAAASASASSIAAISIISIISAAAAAASASASSIAASSIIISIS";
        string[] values = new string[] { "False", "0", "0", "0", "0", "\u0000", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0", null, null, "0", null, null, null, null, "True", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "True", null, null, null, null, null, "True", null, "1", null, "1", null, null, "1", null, "1", null, "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, "1", "1", "1", "\u0001", "1", "1", "1", "1", "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, "1", null, null, "1", null, "1", null, "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, "1", "\u0001", "1", "1", "1", "1", "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, "65", "A", "65", "65", "65", "65", "65", "65", null, null, null, null, "A", null, null, null, null, null, "A", null, null, null, null, null, null, "1", null, "1", null, "1", "1", null, null, null, null, "1", null, null, "1", null, null, "1", null, null, null, null, null, null, null, "1", "1", "1", "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, "1", null, "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, null, "1", "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, null, null, "1", "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, null, null, null, "1", null, null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, null, null, null, null, "1", null, null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, null, null, null, null, null, "1", null, null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, null, null, null, null, null, null, null, null, "1", null, "1", null, null, null, null, null, "1", null, null, null, null, null, null, "1", null, "1", null, "1", "1", null, null, null, "One", "One", null, null, null, "One", null, "One", null, null, null, null, null, null, "1", null, "1", null, "1", "1", null, null, null, null, "One", null, null, null, null, null, "One", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "x", "x", null, null, null, null, "x", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, typeof(object).ToString(), null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "7", null, "7", null, null, "7", null, null, null, null, null, null, null, "1", null, "1", null, "1", "1", null, null, null, null, "1", null, null, "1", null, null, "1", null, null, null, null, null, null, "1", null, "1", null, "1", "1", null, null, null, "One", "One", null, null, null, "One", null, "One" };
        var cell = 0;
        foreach (var source in sources) for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            var target = targets[targetIndex];
            var outcome = 'S';
            try { target.Value.SetValue(source.Value, 0); }
            catch (ArgumentException) { outcome = 'A'; }
            catch (InvalidCastException) { outcome = 'I'; }
            if (outcome != expected[cell]) return 1000 + cell;
            if (outcome == 'S' && Read(target.Value, targetIndex)?.ToString() != values[cell]) return 2000 + cell;
            cell++;
        }
        return input + 1;
            }
            private static object Read(Array array, int target) => target switch
            {
                    0 => ((bool[])array)[0],
                    1 => ((sbyte[])array)[0],
                    2 => ((byte[])array)[0],
                    3 => ((short[])array)[0],
                    4 => ((ushort[])array)[0],
                    5 => ((char[])array)[0],
                    6 => ((int[])array)[0],
                    7 => ((uint[])array)[0],
                    8 => ((long[])array)[0],
                    9 => ((ulong[])array)[0],
                    10 => ((float[])array)[0],
                    11 => ((double[])array)[0],
                    12 => ((decimal[])array)[0],
                    13 => ((nint[])array)[0],
                    14 => ((nuint[])array)[0],
                    15 => ((Sample[])array)[0],
                    16 => ((object[])array)[0],
                    17 => ((string[])array)[0],
                    18 => ((Record[])array)[0],
                    19 => ((int?[])array)[0],
                    20 => ((Sample?[])array)[0],
                    21 => ((Record?[])array)[0],
                    22 => ((IComparable[])array)[0],
                _ => throw new InvalidOperationException(),
            };
        }
        enum Sample { One = 1 }
        enum Other { One = 1 }
        struct Record { public int Number; public override string ToString() => Number.ToString(); }
        """;
}
