using System;
using TUnit.Core;

namespace NetWasm.System.Text.Json.Tests;

public sealed class JsonConstructorArgumentArrayReducerTests
{
    [Test]
    public void DirectTwoReferenceArgumentsPreserveTheirSlots()
    {
        object?[] arguments = ["first", "second"];
        Func<object?[], ReferencePair> factory = static values =>
            new((string)values[0]!, (string)values[1]!);

        var result = factory(arguments);

        RequireExpectedValues(result);
    }

    [Test]
    public void GenericTwoReferenceArgumentsPreserveTheirSlots()
    {
        var arguments = new object?[2];
        StoreArgument(arguments, 0, "first");
        StoreArgument(arguments, 1, "second");

        Func<object?[], ReferencePair> factory = static values =>
            new((string)values[0]!, (string)values[1]!);
        var result = factory(arguments);

        RequireExpectedValues(result);
    }

    [Test]
    public void ClosedGenericReferenceStaticsHaveIndependentStorage()
    {
        var first = new FirstMarker();
        var second = new SecondMarker();

        GenericStaticSlot<FirstMarker>.Value = first;
        GenericStaticSlot<SecondMarker>.Value = second;

        if (!ReferenceEquals(GenericStaticSlot<FirstMarker>.Value, first) ||
            !ReferenceEquals(GenericStaticSlot<SecondMarker>.Value, second))
        {
            throw new InvalidOperationException("Closed generic reference statics shared storage.");
        }
    }

    [Test]
    public void ClosedGenericTypedStaticsHaveIndependentStorage()
    {
        var first = new FirstMarker();
        var second = new SecondMarker();

        TypedGenericStaticSlot<FirstMarker>.Value = first;
        TypedGenericStaticSlot<SecondMarker>.Value = second;

        if (!ReferenceEquals(TypedGenericStaticSlot<FirstMarker>.Value, first) ||
            !ReferenceEquals(TypedGenericStaticSlot<SecondMarker>.Value, second))
        {
            throw new InvalidOperationException("Closed generic typed statics shared storage.");
        }
    }

    [Test]
    public void LargeMixedFramePreservesFieldsThroughGenericCopy()
    {
        var frame = CreateMixedFrame();

        var copy = Copy(frame);

        RequireExpectedFrame(copy);
    }

    [Test]
    public void LargeMixedFramePreservesFieldsThroughArrayStorage()
    {
        var frames = new[] { CreateMixedFrame() };

        var copy = frames[0];

        RequireExpectedFrame(copy);
    }

    [Test]
    public void HighArityReferenceConstructorPreservesArguments()
    {
        object?[] arguments = ["r00", "r01", "r02", "r03", "r04", "r05", "r06", "r07", "r08", "r09", "r10", "r11"];
        Func<object?[], HighArityReferenceProjection> factory = static values =>
            new((string)values[0]!, (string)values[1]!, (string)values[2]!, (string)values[3]!, (string)values[4]!, (string)values[5]!, (string)values[6]!, (string)values[7]!, (string)values[8]!, (string)values[9]!, (string)values[10]!, (string)values[11]!);

        var result = factory(arguments);

        if (result.Value00 != "r00" ||
            result.Value01 != "r01" ||
            result.Value02 != "r02" ||
            result.Value03 != "r03" ||
            result.Value04 != "r04" ||
            result.Value05 != "r05" ||
            result.Value06 != "r06" ||
            result.Value07 != "r07" ||
            result.Value08 != "r08" ||
            result.Value09 != "r09" ||
            result.Value10 != "r10" ||
            result.Value11 != "r11")
        {
            throw new InvalidOperationException("A high-arity reference constructor did not preserve its arguments.");
        }
    }


    [Test]
    public void MixedConstructorShapePreservesArguments()
    {
        var first = new FirstMarker();
        var second = new SecondMarker();
        var items = new global::System.Collections.Generic.List<string> { "item" };
        var map = new global::System.Collections.Generic.Dictionary<string, int> { ["key"] = 42 };
        object?[] arguments = ["text", 7, first, items, map, second];
        Func<object?[], MixedShapeProjection> factory = static values =>
            new(
                (string)values[0]!,
                (int?)values[1],
                (FirstMarker)values[2]!,
                (global::System.Collections.Generic.List<string>)values[3]!,
                (global::System.Collections.Generic.Dictionary<string, int>)values[4]!,
                (SecondMarker)values[5]!);

        var result = factory(arguments);

        if (result.Text != "text" || result.Number != 7 ||
            !ReferenceEquals(result.First, first) || !ReferenceEquals(result.Second, second) ||
            result.Items.Count != 1 || result.Items[0] != "item" ||
            result.Map.Count != 1 || result.Map["key"] != 42)
        {
            throw new InvalidOperationException("A mixed constructor shape did not preserve its arguments.");
        }
    }


    private static void StoreArgument<T>(object?[] arguments, int index, T value) =>
        arguments[index] = value;

    private static T Copy<T>(T value) => value;

    private static MixedFrame CreateMixedFrame() =>
        new()
        {
            Reference00 = "r00",
            Value00 = 100,
            Reference01 = "r01",
            Value01 = 101,
            Reference02 = "r02",
            Value02 = 102,
            Reference03 = "r03",
            Value03 = 103,
            Reference04 = "r04",
            Value04 = 104,
            Reference05 = "r05",
            Value05 = 105,
            Reference06 = "r06",
            Value06 = 106,
            Reference07 = "r07",
            Value07 = 107,
            Reference08 = "r08",
            Reference09 = "r09",
            Reference10 = "r10",
            Reference11 = "r11",
            Nested00 = new NestedFrame("n00", 200),
            Nested01 = new NestedFrame("n01", 201),
            Nested02 = new NestedFrame("n02", 202),
            Nested03 = new NestedFrame("n03", 203),
            Flag00 = true,
            Flag01 = false,
            Long00 = 300,
            Long01 = 301,
            Double00 = 400.5,
            Double01 = 401.5,
            Tail00 = "tail00",
            Tail01 = "tail01",
            Tail02 = "tail02",
            Tail03 = "tail03",
        };

    private static void RequireExpectedFrame(MixedFrame frame)
    {
        if (!Equals(frame.Reference00, "r00") || frame.Value00 != 100 ||
            !Equals(frame.Reference01, "r01") || frame.Value01 != 101 ||
            !Equals(frame.Reference02, "r02") || frame.Value02 != 102 ||
            !Equals(frame.Reference03, "r03") || frame.Value03 != 103 ||
            !Equals(frame.Reference04, "r04") || frame.Value04 != 104 ||
            !Equals(frame.Reference05, "r05") || frame.Value05 != 105 ||
            !Equals(frame.Reference06, "r06") || frame.Value06 != 106 ||
            !Equals(frame.Reference07, "r07") || frame.Value07 != 107 ||
            !Equals(frame.Reference08, "r08") || !Equals(frame.Reference09, "r09") ||
            !Equals(frame.Reference10, "r10") || !Equals(frame.Reference11, "r11") ||
            frame.Nested00 != new NestedFrame("n00", 200) ||
            frame.Nested01 != new NestedFrame("n01", 201) ||
            frame.Nested02 != new NestedFrame("n02", 202) ||
            frame.Nested03 != new NestedFrame("n03", 203) ||
            !frame.Flag00 || frame.Flag01 ||
            frame.Long00 != 300 || frame.Long01 != 301 ||
            frame.Double00 != 400.5 || frame.Double01 != 401.5 ||
            frame.Tail00 != "tail00" || frame.Tail01 != "tail01" ||
            frame.Tail02 != "tail02" || frame.Tail03 != "tail03")
        {
            throw new InvalidOperationException("A large mixed frame did not preserve its fields.");
        }
    }

    private static void RequireExpectedValues(ReferencePair result)
    {
        if (result.First != "first" || result.Second != "second")
        {
            throw new InvalidOperationException("Two reference arguments did not preserve their assigned slots.");
        }
    }

    private sealed record ReferencePair(string First, string Second);

    private sealed class FirstMarker;

    private sealed class SecondMarker;

    private sealed record MixedShapeProjection(
        string Text,
        int? Number,
        FirstMarker First,
        global::System.Collections.Generic.List<string> Items,
        global::System.Collections.Generic.Dictionary<string, int> Map,
        SecondMarker Second);


    private sealed record HighArityReferenceProjection(string Value00, string Value01, string Value02, string Value03, string Value04, string Value05, string Value06, string Value07, string Value08, string Value09, string Value10, string Value11);


    private readonly record struct NestedFrame(string? Reference, int Value);

    private struct MixedFrame
    {
        public object? Reference00;
        public int Value00;
        public object? Reference01;
        public int Value01;
        public object? Reference02;
        public int Value02;
        public object? Reference03;
        public int Value03;
        public object? Reference04;
        public int Value04;
        public object? Reference05;
        public int Value05;
        public object? Reference06;
        public int Value06;
        public object? Reference07;
        public int Value07;
        public object? Reference08;
        public object? Reference09;
        public object? Reference10;
        public object? Reference11;
        public NestedFrame Nested00;
        public NestedFrame Nested01;
        public NestedFrame Nested02;
        public NestedFrame Nested03;
        public bool Flag00;
        public bool Flag01;
        public long Long00;
        public long Long01;
        public double Double00;
        public double Double01;
        public string? Tail00;
        public string? Tail01;
        public string? Tail02;
        public string? Tail03;
    }

    private static class GenericStaticSlot<T>
    {
        public static object? Value;
    }

    private static class TypedGenericStaticSlot<T>
        where T : class
    {
        public static T? Value;
    }
}
