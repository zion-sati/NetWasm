namespace NetWasm.Compiler.Tests.Correctness;

// Converting one delegate to another (new D(other), or other.Invoke as a method group) binds
// the new delegate to the other's Invoke: ldftn D2::Invoke, then newobj (issue 223). A
// delegate's Invoke is the runtime's, so its address is its type's invoke helper, which runs
// the other delegate, an invocation list included.
public sealed class DelegateInvokeTargetCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ADelegateBoundToAnotherDelegatesInvokeRunsIt() =>
        runner.Run(new("DelegateInvokeTarget", "DelegateInvokeTarget", Source, [0, 1, 2, 3, 4, 5, 6, 7])
        {
            Matrix = new(CorpusMatrixProfile.Extended),
        });

    private const string Source = """
        using System;
        namespace DelegateInvokeTarget;

        public delegate int Transform(int value);

        public readonly record struct Pair(int Left, string Right);

        public static class EntryPoint
        {
            private static int calls;

            public static int Trace() => 0;

            private static void Count() => calls++;

            private static void CountTwice() => calls += 2;

            public static int Run(int input)
            {
                Func<int, int> twice = x => x * 2;
                switch (input)
                {
                    case 0:
                    {
                        // The issue's program.
                        Func<int, int> wrapped = new Func<int, int>(twice);
                        return wrapped(21);
                    }
                    case 1:
                    {
                        // Another delegate type with the same signature.
                        Transform transform = new Transform(twice);
                        Func<int, int> back = new Func<int, int>(transform);
                        return transform(5) * 100 + back(7);
                    }
                    case 2:
                    {
                        // A method group of Invoke.
                        Func<int, int> group = twice.Invoke;
                        Converter<int, int> converter = group.Invoke;
                        return converter(input + 10);
                    }
                    case 3:
                    {
                        // A wrapper runs the whole invocation list it binds.
                        calls = 0;
                        Action list = Count;
                        list += CountTwice;
                        list += Count;
                        var wrapped = new Action(list);
                        wrapped();
                        return calls;
                    }
                    case 4:
                    {
                        // A wrapper of a wrapper.
                        var first = new Func<int, int>(twice);
                        var second = new Func<int, int>(first);
                        return second(input) + first(1);
                    }
                    case 5:
                    {
                        // Wrappers of one delegate are equal; a wrapper is not the delegate it wraps.
                        var first = new Func<int, int>(twice);
                        var second = new Func<int, int>(twice);
                        return (first.Equals(second) ? 1 : 0) + (first.Equals(twice) ? 10 : 0) +
                            (first.GetHashCode() == second.GetHashCode() ? 100 : 0);
                    }
                    case 6:
                    {
                        // Generic arguments and a value-type result.
                        Func<string, int, Pair> make = (text, value) => new Pair(value, text);
                        var wrapped = new Func<string, int, Pair>(make);
                        var pair = wrapped("pair", input);
                        return pair.Left * 10 + pair.Right.Length;
                    }
                    default:
                    {
                        // Wrapping a delegate that captures state.
                        var offset = input * 3;
                        Func<int, int> add = x => x + offset;
                        Func<int, int> wrapped = add.Invoke;
                        return wrapped(input);
                    }
                }
            }
        }
        """;
}
