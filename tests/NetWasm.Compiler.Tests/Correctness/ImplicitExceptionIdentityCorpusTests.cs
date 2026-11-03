using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A null, range, cast, arithmetic or array store check throws a new exception
// each time, as desktop does: two throws are two objects, Data set on one is
// not on the next and survives a collection, and Message and HResult are a
// constructed exception's.
public sealed class ImplicitExceptionIdentityCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void ImplicitExceptionsAreNewObjects(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ImplicitExceptionIdentityCorpus",
            "NetWasm.Correctness.ImplicitExceptionIdentity",
            Source,
            [.. Enumerable.Range(0, 12)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            #nullable disable
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.ImplicitExceptionIdentity;

            public sealed class Box
            {
                public int Value;
            }

            public static class EntryPoint
            {
                private static readonly int[] s_three = new int[3];
                private static object s_sink;

                public static int Run(int input) => input switch
                {
                    0 => Distinct(CallOnNull, FieldOfNull),
                    1 => Distinct(ElementOfNull, CallOnNull),
                    2 => Distinct(OutOfRange, OutOfRange),
                    3 => Distinct(DivideByZero, RemainderByZero),
                    4 => Distinct(BadCast, BadUnbox),
                    5 => Distinct(CheckedOverflow, CheckedOverflow),
                    6 => Distinct(StoreMismatch, StoreMismatch),
                    7 => DataNotShared(),
                    8 => DataSurvivesCollection(CallOnNull),
                    9 => DataSurvivesCollection(OutOfRange),
                    10 => Constructed(),
                    11 => DataSurvivesCollection(DivideByZero) + DataSurvivesCollection(BadCast),
                    _ => -1,
                };

                // 1: two objects; 2: of one type.
                private static int Distinct(Func<Exception> first, Func<Exception> second)
                {
                    var a = first();
                    var b = second();
                    return (ReferenceEquals(a, b) ? 0 : 1) + (a.GetType() == b.GetType() ? 2 : 0);
                }

                private static int DataNotShared()
                {
                    var a = CallOnNull();
                    a.Data["k"] = 1;
                    var b = ElementOfNull();
                    var c = OutOfRange();
                    c.Data["r"] = 2;
                    var d = OutOfRange();
                    return (b.Data.Contains("k") ? 0 : 1) + (b.Data.Count == 0 ? 2 : 0) +
                        (d.Data.Contains("r") ? 0 : 4) + (a.Data.Count == 1 ? 8 : 0);
                }

                private static int DataSurvivesCollection(Func<Exception> fault)
                {
                    var a = fault();
                    a.Data["k"] = 42;
                    a.Data["s"] = "kept";
                    for (var i = 0; i < 4000; i++) s_sink = new byte[1024];
                    GC.Collect();
                    var b = fault();
                    return (int)a.Data["k"] * 100 + ((string)a.Data["s"]).Length * 10 + a.Data.Count + (b.Data.Count == 0 ? 1000 : 0);
                }

                private static int Constructed()
                {
                    var result = 0;
                    Exception[] thrown = [CallOnNull(), OutOfRange(), DivideByZero(), BadCast(), CheckedOverflow(), StoreMismatch()];
                    Exception[] made =
                    [
                        new NullReferenceException(), new IndexOutOfRangeException(), new DivideByZeroException(),
                        new InvalidCastException(), new OverflowException(), new ArrayTypeMismatchException(),
                    ];
                    for (var i = 0; i < thrown.Length; i++)
                    {
                        result |= (thrown[i].GetType() == made[i].GetType() ? 1 : 0) << (3 * i);
                        result |= (thrown[i].HResult == made[i].HResult ? 1 : 0) << (3 * i + 1);
                        result |= (thrown[i].InnerException is null && thrown[i].Data.Count == 0 ? 1 : 0) << (3 * i + 2);
                    }
                    return result;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Null<T>() where T : class => null;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Value(int value) => value;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static object Boxed(object value) => value;

                private static Exception Catch(Action action)
                {
                    try
                    {
                        action();
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                private static Exception CallOnNull() => Catch(static () => s_sink = Null<object>().ToString());
                private static Exception FieldOfNull() => Catch(static () => s_sink = Null<Box>().Value);
                private static Exception ElementOfNull() => Catch(static () => s_sink = Null<int[]>()[0]);
                private static Exception OutOfRange() => Catch(static () => s_sink = s_three[Value(5)]);
                private static Exception DivideByZero() => Catch(static () => s_sink = 7 / Value(0));
                private static Exception RemainderByZero() => Catch(static () => s_sink = 7 % Value(0));
                private static Exception BadCast() => Catch(static () => s_sink = (string)Boxed(3));
                private static Exception BadUnbox() => Catch(static () => s_sink = (int)Boxed("x"));
                private static Exception CheckedOverflow() => Catch(static () => s_sink = checked(Value(int.MaxValue) + Value(1)));
                private static Exception StoreMismatch() => Catch(static () => ((object[])Boxed(new string[1]))[0] = Boxed(1));

                public static int Trace() => 0;
            }
            """;
}
