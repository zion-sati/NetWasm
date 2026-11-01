namespace NetWasm.Compiler.Tests.Correctness;

public sealed class UnsafeAccessorNestedGenericTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NestedOwnersKeepTypeAndMethodParametersDistinct() =>
        runner.Run(new("NestedAccessorContracts", "NestedAccessorContracts", Source, [42])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace NestedAccessorContracts;

        public class Outer<T> where T : struct
        {
            public sealed class Inner<U> where U : class
            {
                private T first;
                private U second;
                private static U shared = null!;
                private Inner(T first, U second) { this.first = first; this.second = second; }
                public T First => first;
                public U Second => second;
                public static U Shared => shared;
                private V Swap<V>(ref T item, in U next, out U previous, V result) where V : class, new()
                {
                    var old = first; first = item; item = old;
                    previous = second; second = next;
                    return result;
                }
            }
        }

        public static class Accessors<T> where T : struct
        {
            public static class Nested<U> where U : class
            {
                [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
                public static extern Outer<T>.Inner<U> Create(T first, U second);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Swap")]
                public static extern V Swap<V>(Outer<T>.Inner<U> owner, ref T item, in U next,
                    out U previous, V result) where V : class, new();
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "first")]
                public static extern ref T Field(Outer<T>.Inner<U> owner);
                [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "shared")]
                public static extern ref U Shared(Outer<T>.Inner<U> owner);
            }
        }

        public sealed class Marker { public int Value; }
        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                var original = new Marker { Value = 1 };
                var next = new Marker { Value = 2 };
                var result = new Marker { Value = input };
                var owner = Accessors<int>.Nested<Marker>.Create(7, original);
                var item = 11;
                if (!ReferenceEquals(result, Accessors<int>.Nested<Marker>.Swap(owner, ref item, in next,
                    out var previous, result))) return 1;
                if (item != 7 || owner.First != 11 || !ReferenceEquals(previous, original) ||
                    !ReferenceEquals(owner.Second, next)) return 2;
                Accessors<int>.Nested<Marker>.Field(owner) = input;
                Accessors<int>.Nested<Marker>.Shared(null!) = result;
                if (owner.First != input || !ReferenceEquals(Outer<int>.Inner<Marker>.Shared, result)) return 3;
                return result.Value;
            }
        }
        """;
}
