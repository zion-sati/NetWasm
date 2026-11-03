using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Field accesses whose object an earlier access already checked skip their
// null check; a null object still throws at the access desktop throws at:
// one that no earlier access dominates, one after the slot is assigned or
// changed through a ref, and each step of a list walk. The effects before the
// throw stay, and the instance methods that read several fields of `this`
// read them all.
public sealed class FieldNullCheckCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void FieldNullChecksMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "FieldNullCheckCorpus",
            "NetWasm.Correctness.FieldNullChecks",
            Source,
            [.. Enumerable.Range(0, 7)])
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

            namespace NetWasm.Correctness.FieldNullChecks;

            public sealed class Node
            {
                public int A, B;
                public Node Next;

                [MethodImpl(MethodImplOptions.NoInlining)]
                public int Sum() => A * 3 + B + (Next is null ? 0 : Next.A);

                [MethodImpl(MethodImplOptions.NoInlining)]
                public void Shift()
                {
                    A = B;
                    B = A + 1;
                    if (Next is not null) Next.B = A;
                }
            }

            public static class EntryPoint
            {
                private static int s_effects;

                public static int Run(int input)
                {
                    var list = new Node { A = 1, B = 2, Next = new Node { A = 3, B = 4, Next = new Node { A = 5, B = 6 } } };
                    s_effects = 0;
                    var value = Guard(() => input switch
                    {
                        0 => Dominated(list, true) + Dominated(list, false),
                        1 => Dominated(null, false),
                        2 => Dominated(null, true),
                        3 => Reassigned(list, null),
                        4 => Exposed(list),
                        5 => Walk(list) + Walk(new Node { A = 7, Next = null }),
                        6 => Instance(list) + Instance(null),
                        _ => throw new ArgumentOutOfRangeException(nameof(input)),
                    });
                    return unchecked(value * 31 + s_effects);
                }

                private static int Guard(Func<int> run)
                {
                    try { return run(); }
                    catch (NullReferenceException) { return -1; }
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Dominated(Node node, bool early)
                {
                    var result = 0;
                    if (early)
                    {
                        s_effects++;
                        result = node.A;
                    }
                    s_effects += 10;
                    result += node.B;
                    result += node.A * node.B;
                    return result;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Reassigned(Node node, Node other)
                {
                    var result = node.A + node.B;
                    node = other;
                    s_effects += 100;
                    return result + node.B;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Exposed(Node node)
                {
                    var result = node.A;
                    Clear(ref node);
                    s_effects += 1000;
                    return result + node.B;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Clear(ref Node node) => node = null;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Walk(Node node)
                {
                    var sum = 0;
                    while (true)
                    {
                        sum += node.A;
                        s_effects++;
                        node = node.Next;
                    }
                }

                private static int Instance(Node node)
                {
                    var sum = node.Sum();
                    node.Shift();
                    return sum * 7 + node.A + node.B + node.Next.B;
                }

                public static int Trace() => 0;
            }
            """;
}
