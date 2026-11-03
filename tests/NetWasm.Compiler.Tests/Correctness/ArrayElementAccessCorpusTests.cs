using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Array.SetValue and the IList indexer store into any element type: a value of the element's
// own type, null as the element's zero, an enum or primitive widened to a primitive element, a
// boxed T into Nullable<T>; anything else throws InvalidCastException, or ArgumentException
// when a primitive does not widen. GetValue and SetValue take any rank. Each case returns the
// value it read back, or a code for the exception it threw.
public sealed class ArrayElementAccessCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void ElementAccessThroughArrayMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ArrayElementAccessCorpus",
            "NetWasm.Correctness.ArrayElementAccess",
            Source,
            [.. Enumerable.Range(0, 32)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Collections;

            namespace NetWasm.Correctness.ArrayElementAccess;

            public enum Shade { None, Light, Dark }
            public struct Pair { public string Name; public int Count; }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Code(() => { Array a = new int[3]; a.SetValue(5, 0); return (int)a.GetValue(0)!; }),
                    1 => Code(() => { IList l = new int[3]; l[1] = 5; return (int)l[1]!; }),
                    2 => Code(() => { Array a = new string[1]; a.SetValue(1, 0); return 0; }),
                    3 => Code(() => { Array a = new long[1]; a.SetValue(5, 0); return (int)(long)a.GetValue(0)!; }),
                    4 => Code(() => { Array a = new int[1]; a.SetValue(5L, 0); return 0; }),
                    5 => Code(() => { Array a = new double[1]; a.SetValue('A', 0); return (int)(double)a.GetValue(0)!; }),
                    6 => Code(() => { Array a = new float[1]; a.SetValue('B', 0); return (int)(float)a.GetValue(0)!; }),
                    7 => Code(() => { var a = new[] { 5 }; ((Array)a).SetValue(null, 0); return a[0]; }),
                    8 => Code(() => { var a = new int?[1]; ((Array)a).SetValue(5, 0); var set = a[0] ?? -1; ((Array)a).SetValue(null, 0); return set * 10 + (a[0] is null ? 1 : 0); }),
                    9 => Code(() => { Array a = new int?[1]; a.SetValue(5L, 0); return 0; }),
                    10 => Code(() => { Array a = new Shade[1]; a.SetValue(1, 0); return 0; }),
                    11 => Code(() => { var a = new int[1]; ((Array)a).SetValue(Shade.Dark, 0); return a[0]; }),
                    12 => Code(() => { var a = new object[1]; ((Array)a).SetValue(5, 0); return (int)a[0]; }),
                    13 => Code(() => { var a = new IComparable[1]; ((Array)a).SetValue("x", 0); ((Array)a).SetValue(new object(), 0); return 0; }),
                    14 => Code(() => { var a = new Pair[1]; ((Array)a).SetValue(new Pair { Name = "abc", Count = 2 }, 0); GC.Collect(); var p = (Pair)((Array)a).GetValue(0)!; return p.Name.Length * 10 + p.Count; }),
                    15 => Code(() => { var a = new int[2, 3]; a.SetValue(7, 1, 2); return (int)a.GetValue(1, 2)! * 10 + a[1, 2]; }),
                    16 => Code(() => { Array a = new int[2, 3]; a.SetValue(7, 1); return 0; }),
                    17 => Code(() => { Array a = new int[2]; a.SetValue(1, 5); return 0; }),
                    18 => Code(() => { Array a = new int[2]; return (int)a.GetValue(-1)!; }),
                    19 => Code(() => { var a = new int[2]; ((Array)a).SetValue(3, 1L); return a[1]; }),
                    20 => Code(() => { Array a = new uint[1]; a.SetValue((short)-1, 0); return 0; }),
                    21 => Code(() => { var a = new ulong[1]; ((Array)a).SetValue((byte)200, 0); return (int)a[0]; }),
                    22 => Code(() => { var a = new float[1]; ((Array)a).SetValue(ulong.MaxValue, 0); return BitConverter.SingleToInt32Bits(a[0]); }),
                    23 => Code(() => { Array a = new int[2, 2]; return (int)a.GetValue(new[] { 0 })!; }),
                    24 => Code(() => { Array a = new bool[1]; a.SetValue(1, 0); return 0; }),
                    25 => Code(() => { Array a = new decimal[1]; a.SetValue(1, 0); return 0; }),
                    26 => Code(() => { var a = new char[1]; ((Array)a).SetValue((byte)65, 0); return a[0]; }),
                    27 => Code(() => { var a = new short[1]; ((Array)a).SetValue((sbyte)-3, 0); return a[0]; }),
                    28 => Code(() => { var a = new Shade?[1]; ((Array)a).SetValue(Shade.Light, 0); return (int)(a[0] ?? Shade.None); }),
                    29 => Code(() => { var a = new long[3, 2, 2]; a.SetValue(9L, new long[] { 2, 1, 1 }); return (int)a[2, 1, 1]; }),
                    30 => Code(() => { var a = new double[1]; ((Array)a).SetValue(1.5f, 0); return (int)(a[0] * 10); }),
                    31 => Code(() => { IList l = new Pair[2]; l[0] = null; l[1] = new Pair { Name = "z", Count = 4 }; return ((Pair)l[1]!).Count + (((Pair)l[0]!).Name is null ? 10 : 0); }),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Code(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (InvalidCastException)
                    {
                        return -100;
                    }
                    catch (ArgumentException)
                    {
                        return -200;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        return -300;
                    }
                }
            }
            """;
}
