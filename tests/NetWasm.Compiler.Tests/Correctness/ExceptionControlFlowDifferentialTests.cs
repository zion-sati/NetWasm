using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class ExceptionControlFlowDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleaseExceptionTransfersMatchDesktopDotNet()
    {
        runner.Run(new(
            "ExceptionControlFlowDifferential",
            "NetWasm.Correctness.Exceptions",
            """
            namespace NetWasm.Correctness.Exceptions;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    try
                    {
                        if (input == 0) return Divide(input);
                        if (input == 1) return ReadNull();
                        if (input == 2) return ReadPastEnd();
                        if (input == 3) return CheckedOverflow(input);
                        if (input == 4) return InvalidCast();
                        if (input == 5) throw new System.ArgumentException();
                        if (input == 6) throw new System.ArgumentOutOfRangeException();
                        if (input == 7) goto Completed;
                        return input;
                    }
                    catch (System.ArgumentOutOfRangeException) when (Filter(input))
                    {
                        _trace += 100;
                        throw;
                    }
                    catch (System.ArgumentException) when (Filter(input))
                    {
                        _trace += 200;
                        return 42;
                    }
                    finally
                    {
                        _trace += 10;
                    }

                Completed:
                    _trace += 1000;
                    return 49;
                }

                public static int Trace() => _trace;

                private static bool Filter(int value)
                {
                    _trace += value;
                    return true;
                }

                private static int Divide(int value) => 100 / value;

                private static int ReadNull()
                {
                    Node? value = null;
                    return value!.Value;
                }

                private static int ReadPastEnd()
                {
                    var values = new[] { 1 };
                    return values[2];
                }

                private static int CheckedOverflow(int value) =>
                    checked(int.MaxValue + value);

                private static int InvalidCast()
                {
                    object value = new Base();
                    return ((Derived)value).Value;
                }
            }

            public sealed class Node
            {
                public int Value;
            }

            public class Base;

            public sealed class Derived : Base
            {
                public int Value = 1;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 41])
        {
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty
                .Add(5, 42).Add(7, 49).Add(41, 41),
            ExpectedExceptionTypes = ImmutableDictionary<int, string>.Empty
                .Add(0, "System.DivideByZeroException")
                .Add(1, "System.NullReferenceException")
                .Add(2, "System.IndexOutOfRangeException")
                .Add(3, "System.OverflowException")
                .Add(4, "System.InvalidCastException")
                .Add(6, "System.ArgumentOutOfRangeException"),
        });
    }
}
