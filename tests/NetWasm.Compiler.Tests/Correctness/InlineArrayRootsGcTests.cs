namespace NetWasm.Compiler.Tests.Correctness;

// A callee can read what its caller holds through a span or a reference the
// caller passes it, so a collection while the callee runs must not free it,
// even when the caller never reads it again. With a collection before every
// allocation, each callee here allocates before it reads.
public sealed class InlineArrayRootsGcTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> Cases() =>
    [
        "ParamsSpanOfObjects", "ParamsSpanOfNodes", "SpanOfDeclaredInlineArray", "LocalSpanOfObjects", "SpanWrittenByCallee", "RefFieldWrittenByCallee",
        "RefToLocal", "InToStructLocal", "OutToLocal", "RefToArrayElement", "RefToField", "SpanOfArray",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhatACalleeReadsThroughItsArgumentsSurvivesCollections(string name) =>
        runner.Run(new("InlineArrayRoots" + name, "InlineArrayRoots", Source.Replace("CASE", name, StringComparison.Ordinal), [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        namespace InlineArrayRoots;

        public class Node
        {
            private readonly List<Node> children = [];
            public Node? Parent;
            public string Name;

            public Node(string name) => Name = name;

            public int Count => children.Count;

            public void AddChild(Node node)
            {
                node.Parent?.RemoveChild(node);
                node.Parent = this;
                children.Add(node);
                Name += ".";
            }

            public void RemoveChild(Node node)
            {
                var index = children.IndexOf(node);
                if (index < 0) return;
                children.RemoveAt(index);
                node.Parent = null;
            }

            protected void ReplaceChildren(ReadOnlySpan<Node> nodes)
            {
                while (children.Count > 0) RemoveChild(children[^1]);
                foreach (var node in nodes) AddChild(node);
            }

            public virtual void SetContent(params ReadOnlySpan<Node> nodes) => ReplaceChildren(nodes);
        }

        public sealed class Box : Node
        {
            public Box() : base("box") { }

            public static Box Column(params ReadOnlySpan<Node> children)
            {
                var box = new Box();
                box.SetContent(children);
                return box;
            }
        }

        public sealed class Holder
        {
            public object Field = "";
        }

        public struct Pair
        {
            public string First;
            public int Second;
        }

        [InlineArray(4)]
        public struct Four
        {
            private Pair element;
        }

        [InlineArray(3)]
        public struct Three
        {
            private object element;
        }

        public ref struct Target
        {
            public ref object Field;
        }

        public static class Cases
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Node Leaf(int value) => new(new string('n', value % 5 + 1));

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Text(char value, int length) => new(value, length);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lengths(params ReadOnlySpan<object> values)
            {
                var length = 0;
                foreach (var value in values) length += Text('g', 3).Length + ((string)value).Length;
                return length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Pairs(ReadOnlySpan<Pair> pairs)
            {
                var length = 0;
                foreach (ref readonly var pair in pairs) length += Text('q', 2).Length + pair.First.Length + pair.Second;
                return length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fill(Span<object> values)
            {
                for (var index = 0; index < values.Length; index++) values[index] = Text((char)('j' + index), index + 2);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Store(Target target)
            {
                target.Field = Text('t', 5);
                return Text('u', 1).Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(ref object value) => Text('r', 2).Length + ((string)value).Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(in Pair pair) => Text('i', 2).Length + pair.First.Length + pair.Second;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Write(out object value)
            {
                value = Text('o', 4);
                return Text('w', 2).Length + ((string)value).Length;
            }

            public static int ParamsSpanOfObjects(int input) =>
                Lengths(Text('a', input % 3 + 1), Text('b', 2), Text('c', 3), Text('d', 4), Text('e', 5)) == input % 3 + 1 + 29 ? 0 : 2;

            public static int ParamsSpanOfNodes(int input)
            {
                var column = Box.Column(Leaf(input), Leaf(input + 1), Box.Column(Leaf(input + 2), Leaf(input + 3)), Leaf(input + 4));
                if (column.Count != 4) return 2;
                var inner = Box.Column(Leaf(1), Leaf(2), Leaf(3));
                column.SetContent(inner, Leaf(5), Leaf(6));
                return column.Count == 3 && inner.Count == 3 && inner.Parent == column && column.Name == "box......." ? 0 : 3;
            }

            public static int SpanOfDeclaredInlineArray(int input)
            {
                var four = new Four();
                for (var index = 0; index < 4; index++) four[index] = new Pair { First = Text('p', index + 1), Second = input + index };
                return Pairs(four) == 8 + 10 + 4 * input + 6 ? 0 : 2;
            }

            public static int LocalSpanOfObjects(int input)
            {
                ReadOnlySpan<object> values = [Text('a', 1), Text('b', input % 4 + 2), Text('c', 3)];
                var length = 0;
                foreach (var value in values) length += Text('h', 1).Length + ((string)value).Length;
                return length == 3 + 1 + input % 4 + 2 + 3 ? 0 : 2;
            }

            public static int SpanWrittenByCallee(int input)
            {
                var three = new Three();
                Fill(three);
                var length = 0;
                foreach (var value in three) length += Text('k', 1).Length + ((string)value).Length;
                return length == 3 + 2 + 3 + 4 ? 0 : 2;
            }

            public static int RefFieldWrittenByCallee(int input)
            {
                object value = "";
                var stored = Store(new Target { Field = ref value });
                return stored + Text('v', 1).Length + ((string)value).Length == 1 + 1 + 5 ? 0 : 2;
            }

            public static int RefToLocal(int input)
            {
                object value = Text('l', input % 5 + 1);
                return Read(ref value) == 2 + input % 5 + 1 ? 0 : 2;
            }

            public static int InToStructLocal(int input)
            {
                var pair = new Pair { First = Text('s', 3), Second = input };
                return Read(in pair) == 2 + 3 + input ? 0 : 2;
            }

            public static int OutToLocal(int input) =>
                Write(out var value) + ((string)value).Length + Text('z', 1).Length == 2 + 4 + 4 + 1 ? 0 : 2;

            public static int RefToArrayElement(int input)
            {
                var values = new object[] { Text('e', 2), Text('f', input % 3 + 1) };
                return Read(ref values[1]) == 2 + input % 3 + 1 ? 0 : 2;
            }

            public static int RefToField(int input) =>
                Read(ref new Holder { Field = Text('f', input % 6 + 1) }.Field) == 2 + input % 6 + 1 ? 0 : 2;

            public static int SpanOfArray(int input) =>
                Lengths(new object[] { Text('x', 2), Text('y', input % 2 + 1) }.AsSpan()) == 6 + 2 + input % 2 + 1 ? 0 : 2;
        }

        public static class EntryPoint
        {
            public static object? Probe;

            public static int Trace() => 0;

            public static int Run(int input)
            {
                // On NetWasm, which compiles ahead of time, the case runs with a
                // collection at every allocation; desktop .NET only computes
                // the expected value.
                Probe = new object();
                var collections = GC.CollectionCount(0);
                Probe = new object();
                if (!RuntimeFeature.IsDynamicCodeSupported && GC.CollectionCount(0) == collections) return 1;
                var failure = Cases.CASE(input);
                return failure == 0 ? input : failure;
            }
        }
        """;
}
