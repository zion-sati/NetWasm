using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ArraySetValueBoundsCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void IndicesRanksAndErrorsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "BoundsFixture", Source, "BoundsFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        namespace BoundsFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var one = new int[3];
                one.SetValue((byte)42, 2L);
                if (one[2] != 42) return 1;
                one.SetValue(null, new long[] { 2 });
                if (one[2] != 0) return 2;
                var two = new int[2,3];
                two.SetValue((short)42, 1, 2);
                if (two[1,2] != 42) return 3;
                two.SetValue(43, 1L, 2L);
                if (two[1,2] != 43) return 4;
                two.SetValue(44, new int[] { 1, 2 });
                if (two[1,2] != 44) return 5;
                two.SetValue(null, new long[] { 1, 2 });
                if (two[1,2] != 0) return 6;
                var three = new int[2,2,2];
                three.SetValue(45, 1, 0, 1);
                if (three[1,0,1] != 45) return 7;
                three.SetValue(46, 1L, 0L, 1L);
                if (three[1,0,1] != 46) return 8;
                try { one.SetValue(1, -1); return 9; } catch (IndexOutOfRangeException) { }
                try { one.SetValue(1, 3); return 10; } catch (IndexOutOfRangeException) { }
                try { two.SetValue(1, 0); return 11; } catch (ArgumentException) { }
                try { one.SetValue(1, 0, 0); return 12; } catch (ArgumentException) { }
                try { two.SetValue(1, 0, 0, 0); return 13; } catch (ArgumentException) { }
                try { one.SetValue(1, (int[])null); return 14; } catch (ArgumentNullException e) { if (e.ParamName != "indices") return 15; }
                try { one.SetValue(1, (long[])null); return 16; } catch (ArgumentNullException e) { if (e.ParamName != "indices") return 17; }
                try { one.SetValue(1, long.MaxValue); return 18; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index") return 19; }
                try { two.SetValue(1, 0L, long.MinValue); return 20; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index2") return 21; }
                try { three.SetValue(1, 0L, 0L, long.MaxValue); return 22; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index3") return 23; }
                try { two.SetValue(1, new long[] { long.MaxValue }); return 24; }
                catch (ArgumentOutOfRangeException) { return 25; } catch (ArgumentException) { }
                try { one.SetValue(1, new long[] { long.MaxValue }); return 26; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "index") return 27; }
                try { two.SetValue("bad", new int[] { 0, 3 }); return 28; } catch (IndexOutOfRangeException) { }
                var empty = new int[0,3];
                try { empty.SetValue(1, 0, 0); return 29; } catch (IndexOutOfRangeException) { }
                var reference = new string[] { "before" };
                try { reference.SetValue(new object(), 0); return 30; } catch (InvalidCastException) { }
                if (reference[0] != "before") return 31;
                return input + 1;
            }
        }
        """;
}
