using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class CoreLibScalarCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ObjectDisposedExceptionThrowIfPreservesExceptionClassAndHResult() => runner.Run(new(
            "ObjectDisposedExceptionThrowIf",
            "NetWasm.Correctness.ObjectDisposedExceptionThrowIf",
            """
            using System;

            namespace NetWasm.Correctness.ObjectDisposedExceptionThrowIf;

            public static class EntryPoint
            {
                public static int Trace() => 0;

                public static int Run(int input)
                {
                    var result = 1;
                    ObjectDisposedException.ThrowIf(false, new object());

                    try
                    {
                        ObjectDisposedException.ThrowIf(true, new object());
                    }
                    catch (ObjectDisposedException exception)
                    {
                        if (exception.HResult == unchecked((int)0x80131622))
                        {
                            result |= 2;
                        }
                    }

                    try
                    {
                        ObjectDisposedException.ThrowIf(true, typeof(object));
                    }
                    catch (ObjectDisposedException exception)
                    {
                        if (exception.HResult == unchecked((int)0x80131622))
                        {
                            result |= 4;
                        }
                    }

                    return result;
                }
            }
            """,
            [0])
    {
        ExpectedReturnValue = 7,
    });

    [Fact]
    public void WideAndGenericScalarsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "CoreLibScalarCorpus",
            "NetWasm.Correctness.CoreLibScalars",
            """
            namespace NetWasm.Correctness.CoreLibScalars;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    return input switch
                    {
                        0 => System.Int128.Parse("170141183460469231731687303715884105").ToString().Length,
                        1 => System.UInt128.Parse("340282366920938463463374607431768210").ToString().Length,
                        2 => GenericAndHashContract(),
                        3 => ((System.Half)1.5f + (System.Half)2.25f).GetHashCode(),
                        4 => RandomChecksum(),
                        _ => System.Math.Clamp(input - 7, -4, 4),
                    };
                }

                private static T Add<T>(T left, T right)
                    where T : System.Numerics.IAdditionOperators<T, T, T> => left + right;

                private static int GenericAndHashContract()
                {
                    var value = Add(System.Int128.CreateChecked(13), (System.Int128)7);
                    var equal = System.Int128.Parse("20");
                    return value == equal && value.GetHashCode() == equal.GetHashCode() ? 1 : 0;
                }

                private static int RandomChecksum()
                {
                    var random = new System.Random(12345);
                    return unchecked((random.Next(1, 1000) * 31) + random.Next(1, 1000));
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5])
        {
            // Only this input is a success predicate; the other scalar results
            // are observations compared with desktop, not pass/fail sentinels.
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(2, 1),
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
