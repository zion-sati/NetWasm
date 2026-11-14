namespace NetWasm.Compiler.Tests.Correctness;

// The expression interpreter's member execution allocates before it runs managed code: the object New constructs,
// and the box a struct result is written to. A compiled callee roots only what it still uses, so a constructor
// that is done with `this` and then allocates leaves the object to its caller, which publishes it as the call's
// allocation root, as newobj does. With a collection before every allocation, an unpublished object is freed by
// the constructor's next allocation and reused. (A struct result's buffer is a by-reference argument, which the
// callee keeps rooted; its cases check the boxing.)
public sealed class ExpressionMemberExecutionRootsGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ConstructedObjectsAndStructResultsSurviveCollectionsInTheCall() =>
        runner.Run(new("ExpressionMemberExecutionRoots", "ExpressionMemberExecutionRoots", Source, [0, 1, 2, 3, 4, 5])
        {
            ReportAllMismatches = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Linq.Expressions;
        namespace ExpressionMemberExecutionRoots;

        public struct Pair
        {
            public Pair(string first, string second) { First = first; Second = second; }
            public string First;
            public string Second;
            public static Pair Make(int n) => new Pair("first" + n, "second" + (n * 2));
            public string Join() => First + "|" + Second;
        }

        public sealed class Holder
        {
            public Holder(int n)
            {
                Text = "held" + n;
                Inner = new Pair("inner" + n, Text + "!");
                Churn(n);
            }

            public readonly string Text;
            public Pair Inner;
            public static string Sink = "";

            // `this` is dead here, so only the caller keeps the new object.
            private static void Churn(int n)
            {
                var text = "";
                for (var i = 0; i < 48; i++) text = text + (char)('a' + (i + n) % 26);
                Sink = text;
            }
        }

        public sealed class Log
        {
            public string Text = "";
            public void Add(int n) => Text = Text + "+" + n;
        }

        public static class Statics
        {
            public static Pair Shared = new Pair("shared", "pair");
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                string observed;
                switch (input)
                {
                    case 0:
                    {
                        Expression<Func<int, Pair>> make = n => Pair.Make(n);
                        observed = make.Compile()(7).Join();
                        break;
                    }
                    case 1:
                    {
                        Expression<Func<int, Holder>> create = n => new Holder(n);
                        var holder = create.Compile()(8);
                        observed = holder.Text + holder.Inner.Join();
                        break;
                    }
                    case 2:
                    {
                        Expression<Func<string, Pair>> create = s => new Pair(s + "a", s + "b");
                        observed = create.Compile()("v").Join();
                        break;
                    }
                    case 3:
                    {
                        Expression<Func<Holder, Pair>> inner = h => h.Inner;
                        observed = inner.Compile()(new Holder(9)).Join();
                        break;
                    }
                    case 4:
                    {
                        Expression<Func<Pair>> shared = () => Statics.Shared;
                        Expression<Func<Pair, string>> join = p => p.Join();
                        observed = join.Compile()(shared.Compile()());
                        break;
                    }
                    default:
                    {
                        var log = new Log();
                        Expression<Action<Log, int>> add = (l, n) => l.Add(n);
                        var adder = add.Compile();
                        adder(log, 1);
                        adder(log, 2);
                        observed = log.Text;
                        break;
                    }
                }
                var hash = 17;
                foreach (var c in observed)
                {
                    hash = unchecked(hash * 31 + c);
                }
                return hash & 0x7fffffff;
            }
        }
        """;
}
