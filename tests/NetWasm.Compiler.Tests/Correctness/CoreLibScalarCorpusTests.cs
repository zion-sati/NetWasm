// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CoreLibScalarCorpusTests(CorrectnessTestRunner runner)
{
    // A Clamp whose minimum is above its maximum throws ArgumentException with desktop's message, which names
    // both bounds as they format: a negative bound keeps its sign, a decimal its scale. Each type's own Clamp,
    // Sign and Abs, called directly or through generic math, throws as Math's does, and a type with no Clamp of
    // its own (char) throws through INumber's, which formats its bounds as that type.
    [Fact]
    public void ClampSignAndAbsThrowWithDesktopsMessages() => runner.Run(new(
            "ClampSignAbsMessages",
            "NetWasm.Correctness.ClampSignAbsMessages",
            """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.ClampSignAbsMessages;

            public static class EntryPoint
            {
                private const string NaN = "Function does not accept floating point Not-a-Number values.";
                private const string Negate = "Negating the minimum value of a twos complement number is invalid.";

                public static int Trace() => 0;

                public static int Run(int input)
                {
                    var result = 0;
                    result |= Check<ArgumentException>(() => Math.Clamp(input, input + 5, input + 3), "'5' cannot be greater than 3.", 1 << 0);
                    result |= Check<ArgumentException>(() => Math.Clamp((long)input, input - 1L, input - 5L), "'-1' cannot be greater than -5.", 1 << 1);
                    result |= Check<ArgumentException>(() => Math.Clamp((double)input, input + 3.5, input + 1.5), "'3.5' cannot be greater than 1.5.", 1 << 2);
                    result |= Check<ArgumentException>(() => Math.Clamp((decimal)input, input + 2.50m, input + 1.25m), "'2.50' cannot be greater than 1.25.", 1 << 3);
                    result |= Check<ArgumentException>(() => Half.Clamp((Half)input, (Half)(input + 2.5f), (Half)(input + 0.5f)), "'2.5' cannot be greater than 0.5.", 1 << 4);
                    result |= Check<ArgumentException>(() => Int128.Clamp(input, (Int128)input + 7, (Int128)input + 2), "'7' cannot be greater than 2.", 1 << 5);
                    result |= Check<ArgumentException>(() => UInt128.Clamp((UInt128)input, (UInt128)(input + 9), (UInt128)(input + 4)), "'9' cannot be greater than 4.", 1 << 6);
                    result |= Check<ArgumentException>(() => Math.Clamp((nint)input, (nint)(input + 6), (nint)(input + 1)), "'6' cannot be greater than 1.", 1 << 7);
                    result |= Check<ArgumentException>(() => Math.Clamp((float)input, input + 0.25f, input - 0.75f), "'0.25' cannot be greater than -0.75.", 1 << 8);
                    result |= Check<ArgumentException>(() => int.Clamp(input, input + 5, input + 3), "'5' cannot be greater than 3.", 1 << 9);
                    result |= Check<ArgumentException>(() => ClampOf((char)(input + 'b'), (char)(input + 'b'), (char)(input + 'a')), "'b' cannot be greater than a.", 1 << 10);
                    result |= Check<ArgumentException>(() => ClampOf((float)input, input + 0.25f, input - 0.75f), "'0.25' cannot be greater than -0.75.", 1 << 11);
                    result |= Check<ArgumentException>(() => ClampOf((nuint)input, (nuint)(input + 9), (nuint)(input + 4)), "'9' cannot be greater than 4.", 1 << 12);
                    result |= Check<ArgumentException>(() => ClampOf((sbyte)input, (sbyte)(input - 2), (sbyte)(input - 6)), "'-2' cannot be greater than -6.", 1 << 13);
                    result |= Check<ArgumentException>(() => float.ClampNative(input, input + 2.5f, input + 0.5f), "'2.5' cannot be greater than 0.5.", 1 << 14);
                    result |= Check<ArgumentException>(() => double.ClampNative(input, input + 2.5, input + 0.5), "'2.5' cannot be greater than 0.5.", 1 << 15);
                    result |= Check<ArithmeticException>(() => double.Sign(input / (double)input), NaN, 1 << 16);
                    result |= Check<ArithmeticException>(() => SignOf(input / (float)input), NaN, 1 << 17);
                    result |= Check<OverflowException>(() => AbsOf(int.MinValue + input), Negate, 1 << 18);
                    result |= Check<OverflowException>(() => AbsOf(nint.MinValue + input), Negate, 1 << 19);
                    result |= Check<OverflowException>(() => sbyte.Abs((sbyte)(sbyte.MinValue + input)), Negate, 1 << 20);
                    return result;
                }

                private static T ClampOf<T>(T value, T min, T max) where T : INumber<T> => T.Clamp(value, min, max);

                private static int SignOf<T>(T value) where T : INumber<T> => T.Sign(value);

                private static T AbsOf<T>(T value) where T : INumber<T> => T.Abs(value);

                private static int Check<TException>(Action action, string expected, int bit) where TException : Exception
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception) when (exception.GetType() == typeof(TException))
                    {
                        return exception.Message == expected ? bit : 0;
                    }

                    return 0;
                }
            }
            """,
            [0])
    {
        ExpectedReturnValue = (1 << 21) - 1,
    });

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
