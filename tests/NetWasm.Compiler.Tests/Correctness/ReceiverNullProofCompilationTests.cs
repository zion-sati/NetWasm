// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// An instance method reads its this unchecked when every call of it checks or proves its object. Each shape here
// gives a method's this a way to be null, or a caller a null object, and expects what desktop .NET does: callvirt,
// interface and virtual calls on null throw at the call; a delegate over a null object, a method a delegate binds,
// and a method that calls a sibling with its own this keep the checks that make a null this throw where desktop's
// does.
public sealed class ReceiverNullProofCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NullReceiversThrowWhereDesktopThrows() =>
        runner.Run(new("ReceiverNullProofs", "ReceiverNullProofs", Source, [0, 1, 2, 3])
        {
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace ReceiverNullProofs;

        public interface IShape
        {
            int Area();
        }

        public class Node : IShape
        {
            public int A = 3;
            public Node? Next;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Size() => A + Helper();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private int Helper() => A * 2;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Bound() => A + Helper();

            [MethodImpl(MethodImplOptions.NoInlining)]
            public virtual int Kind() => A;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Area() => A * A;
        }

        public sealed class Leaf : Node
        {
            public int B = 5;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public override int Kind() => B + base.Kind();
        }

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            public static int Run(int input)
            {
                Node? node = input % 2 == 0 ? new Node() : null;
                Node? leaf = input < 2 ? new Leaf() : null;
                var hash = 17;
                hash = Mix(hash, Guard(() => node!.Size()));
                hash = Mix(hash, Guard(() => leaf!.Kind()));
                hash = Mix(hash, Guard(() => ((IShape)node!).Area()));
                hash = Mix(hash, Guard(() => { Func<int> bound = node!.Bound; return bound(); }));
                hash = Mix(hash, Guard(() => Next(node)));
                hash = Mix(hash, Guard(() => new Leaf().Kind() + new Node().Size()));
                _trace = hash;
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Next(Node? node) => node!.Next!.Size();

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            private static int Guard(Func<int> run)
            {
                try
                {
                    return run();
                }
                catch (Exception exception)
                {
                    return exception.GetType() == typeof(NullReferenceException) ? -1
                        : exception.GetType() == typeof(ArgumentException) ? -2 : -3;
                }
            }
        }
        """;
}
