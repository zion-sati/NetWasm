using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A callvirt checks its receiver for null after its arguments are evaluated
// and before the callee runs. A callee inlined because its first effect reads
// a property of the receiver, through the accessor or a callvirt, must throw
// NullReferenceException at the same point.
[Collection(CorrectnessTestGroup.Name)]
public sealed class InlinedReceiverCheckTests(CorrectnessTestRunner runner)
{
    // Oz inlines the calls inside the loop; O3 inlines the one after it too.
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void ACalleeReadingAReceiverPropertyFirstFaultsLikeTheCall(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "InlinedReceiverCheck" + optimization,
            "NetWasm.Correctness.InlinedReceiverCheck",
            Source,
            [0, 1, 4])
        {
            Optimization = optimization,
        });

    private const string Source =
            """
            using System;

            namespace NetWasm.Correctness.InlinedReceiverCheck;

            public class Shape
            {
                public int Width { get; set; }
                public virtual int Depth => 3;

                public int Area(int scale) => Width * scale + 1;

                public int Volume(int scale) => Depth * scale + Width;

                // Its first effect is no receiver read, so a null receiver must
                // fault before the count changes.
                public int Counted(int scale)
                {
                    EntryPoint.Count++;
                    return Width * scale;
                }
            }

            public sealed class Box : Shape
            {
                public override int Depth => 5;
            }

            public static class EntryPoint
            {
                private static int s_arguments;

                public static int Count;

                public static int Run(int input)
                {
                    Shape?[] shapes = [new Shape { Width = input }, null, new Box { Width = input + 2 }, null];
                    var sum = 0;
                    var faults = 0;
                    for (var index = 0; index < shapes.Length * 3; index++)
                    {
                        var shape = shapes[index % shapes.Length];
                        try
                        {
                            sum += shape!.Area(Argument(index));
                        }
                        catch (NullReferenceException)
                        {
                            faults += 1;
                        }
                        try
                        {
                            sum += shape!.Volume(Argument(index));
                        }
                        catch (NullReferenceException)
                        {
                            faults += 100;
                        }
                        try
                        {
                            sum += shape!.Counted(Argument(index));
                        }
                        catch (NullReferenceException)
                        {
                            faults += 1000;
                        }
                    }
                    try
                    {
                        sum += shapes[input & 1]!.Area(Argument(input));
                    }
                    catch (NullReferenceException)
                    {
                        faults += 10000;
                    }
                    return unchecked(sum * 1000003 + faults * 31 + s_arguments * 7 + Count);
                }

                // Arguments are evaluated before the receiver check faults.
                private static int Argument(int value)
                {
                    s_arguments++;
                    return value + 1;
                }

                public static int Trace() => 0;
            }
            """;
}
