namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class ValueAndCallDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleaseValuesStorageAndDispatchMatchDesktopDotNet()
    {
        runner.Run(new(
            "ValueAndCallDifferential",
            "NetWasm.Correctness.Values",
            """
            namespace NetWasm.Correctness.Values;

            public interface ICompute
            {
                int Apply(int value);
            }

            public interface ITransform<T>
            {
                T Transform(T value);
            }

            public class BaseCompute
            {
                public virtual int Apply(int value) => value + 1;
            }

            public sealed class DerivedCompute : BaseCompute, ICompute
            {
                public override int Apply(int value) => value + 2;
            }

            public struct Pair
            {
                public int Number;
                public object? Tag;

                public Pair(int number, object? tag)
                {
                    Number = number;
                    Tag = tag;
                }
            }

            public sealed class PairTransform : ITransform<Pair>
            {
                public Pair Transform(Pair value) =>
                    new(value.Number + 3, value.Tag);
            }

            public sealed class Holder
            {
                public Pair Value;
            }

            public sealed class Box<T>
            {
                public T Value;

                public Box(T value) => Value = value;
            }

            public delegate int Compute(int value);

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    var signed = input + 2 - 1;
                    var unsigned = unchecked((uint)input) + 3U;
                    var wide = (long)input * 2L + (long)(ulong)4;
                    var floating = (int)((float)input + 1.5f);
                    var doubled = (int)((double)input + 2.5d);
                    var wrapped = unchecked(int.MaxValue + input);
                    var bounded = checked(input + 10);

                    var local = new Pair(input, new object());
                    var holder = new Holder { Value = local };
                    var values = new Pair[2];
                    values[0] = holder.Value;
                    values[1] = Make(input + 1, local.Tag);
                    object boxed = values[1];
                    var unboxed = (Pair)boxed;
                    var generic = new Box<Pair>(Identity(unboxed));
                    ITransform<Pair> transform = new PairTransform();
                    var transformed = transform.Transform(generic.Value);

                    BaseCompute virtualTarget = new DerivedCompute();
                    ICompute interfaceTarget = (DerivedCompute)virtualTarget;
                    Compute calls = First;
                    calls += Second;
                    var delegateResult = calls(input);

                    var total = signed + (int)(unsigned & 15U) + (int)(wide & 31L) +
                        floating + doubled + (wrapped & 7) + bounded +
                        Read(values[0]) + transformed.Number +
                        virtualTarget.Apply(input) + interfaceTarget.Apply(input) +
                        delegateResult;
                    _trace += total + (local.Tag is null ? 1000 : 0);
                    return total;
                }

                public static int Trace() => _trace;

                private static T Identity<T>(T value) => value;

                private static Pair Make(int value, object? tag) => new(value, tag);

                private static int Read(Pair value) => value.Number;

                private static int First(int value)
                {
                    _trace += 10;
                    return value + 4;
                }

                private static int Second(int value)
                {
                    _trace += 100;
                    return value + 5;
                }
            }
            """,
            [-10, 0, 1, 21, 41]));
    }
}
