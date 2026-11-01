namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AttributeLifetimeSemanticsTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void QueriesCreateFreshObjectsAndPayloadsWhileEnumerationReusesResults() => runner.Run(new(
        "AttributeLifetimes", "AttributeLifetimes", Source, [42])
    {
        ExpectedReturnValue = 42,
        Matrix = new(CorpusMatrixProfile.Family),
    });

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeLifetimes;

        public class MarkerBaseAttribute<T> : Attribute
        {
            public virtual T Value { get; set; } = default!;
        }
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
        public sealed class MarkerAttribute<T>(int[] values) : MarkerBaseAttribute<T>
        {
            public static int Calls;
            public int[] Values { get; } = values;
            public int Nested { get; } = typeof(NestedTarget).GetCustomAttribute<NestedAttribute>()!.Value;
            public override T Value
            {
                get => base.Value;
                set { Calls++; base.Value = value; }
            }
        }
        public sealed class NestedAttribute(int value) : Attribute
        {
            public int Value { get; } = value;
        }
        [Nested(17)] public class NestedTarget;
        [Marker<int>(new[] { 3, 5 }, Value = 7)] public class Target;

        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                var first = typeof(Target).GetCustomAttribute<MarkerAttribute<int>>()!;
                var second = typeof(Target).GetCustomAttribute<MarkerAttribute<int>>()!;
                if (ReferenceEquals(first, second) || ReferenceEquals(first.Values, second.Values)) return 1;
                first.Values[0] = 99;
                if (second.Values[0] != 3 || first.Value != 7 || second.Nested != 17 ||
                    MarkerAttribute<int>.Calls != 2) return 2;
                var results = typeof(Target).GetCustomAttributes<MarkerAttribute<int>>();
                var calls = MarkerAttribute<int>.Calls;
                var enumerator = results.GetEnumerator();
                if (!enumerator.MoveNext()) return 3;
                var item = enumerator.Current;
                if (enumerator.MoveNext()) return 4;
                enumerator.Dispose();
                enumerator = results.GetEnumerator();
                if (!enumerator.MoveNext() || !ReferenceEquals(item, enumerator.Current) ||
                    enumerator.Current.Values[0] != 3 || MarkerAttribute<int>.Calls != calls) return 5;
                enumerator.Dispose();
                var again = (MarkerAttribute<int>[])typeof(Target).GetCustomAttributes<MarkerAttribute<int>>();
                if (ReferenceEquals(results, again) || ReferenceEquals(item, again[0]) ||
                    ReferenceEquals(item.Values, again[0].Values) || MarkerAttribute<int>.Calls != 4) return 6;
                return input;
            }
        }
        """;
}
