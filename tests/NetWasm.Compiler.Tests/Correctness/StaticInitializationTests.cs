namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class StaticInitializationTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void ConstructorAndValueMethodInitializationPreservePreciseRoots() => Run(new(
        "InstanceInitializationRoots", "NetWasm.Correctness.InstanceInitializationRoots",
        """
        using System.Runtime.CompilerServices;
        namespace NetWasm.Correctness.InstanceInitializationRoots;

        public sealed class Payload { public int Value; }
        public static class Probe { public static int Collections; }
        public interface IRead { int Read(Payload other); }
        public static class StaticOwner<T>
        {
            static StaticOwner() { Probe.Collections++; System.GC.Collect(); }
            public static int Read(Payload first, Payload second) => first.Value + second.Value;
        }
        public sealed class ReferenceOwner
        {
            private readonly Payload _value;
            static ReferenceOwner() { Probe.Collections++; System.GC.Collect(); }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public ReferenceOwner(Payload value) { _value = value; }
            public int Read(Payload other) => _value.Value + other.Value;
        }
        public struct ValueOwner : IRead
        {
            public Payload Value;
            static ValueOwner() { Probe.Collections++; System.GC.Collect(); }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public ValueOwner(Payload value) { Value = value; }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Read(Payload other) => Value.Value + other.Value;
        }
        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadBoxed(IRead value, Payload other) => value.Read(other);
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadConstrained<T>(ref T value, Payload other) where T : struct, IRead
                => value.Read(other);
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadDelegate(ValueOwner value, Payload other)
            {
                System.Func<Payload, int> read = value.Read;
                return read(other);
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadStaticDelegate(Payload first, Payload second)
            {
                System.Func<Payload, Payload, int> read = StaticOwner<int>.Read;
                return read(first, second);
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadExpression(IRead receiver, Payload other)
            {
                System.Linq.Expressions.Expression<System.Func<IRead, int>> expression =
                    item => item.Read(other);
                return expression.Compile()(receiver);
            }
            public static int Run(int input)
            {
                Probe.Collections = 0;
                var live = new Payload { Value = 17 };
                var first = new Payload { Value = 13 };
                var second = new Payload { Value = 12 };
                ValueOwner value = default;
                value.Value = first;
                var result = input switch
                {
                    0 => new ReferenceOwner(first).Read(second),
                    1 => new ValueOwner(first).Read(second),
                    2 => value.Read(second),
                    3 => ReadBoxed(value, second),
                    4 => ReadConstrained(ref value, second),
                    5 => ReadDelegate(value, second),
                    6 => ReadStaticDelegate(first, second),
                    _ => ReadExpression(value, second),
                };
                return Probe.Collections == 1 ? result + live.Value : -1;
            }
            public static int Trace() => 0;
        }
        """, [0, 1, 2, 3, 4, 5, 6, 7])
    {
        ExpectedReturnValue = 42,
        RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
        Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
    });

    // A precise type initializes before its constructors run, so its instance
    // methods read its statics without initialization checks; a subclass's
    // constructor initializes the base type through the base constructor call.
    [Fact]
    public void PreciseInstanceMethodsReadTheirOwnStaticsAfterInitialization() => Run(new(
        "PreciseInstanceStatics", "NetWasm.Correctness.PreciseInstanceStatics",
        """
        using System.Runtime.CompilerServices;
        namespace NetWasm.Correctness.PreciseInstanceStatics;

        public sealed class Payload { public int Value; }
        public static class Probe { public static int Order; }
        public class Counter
        {
            private static int s_total;
            private static readonly Payload[] s_weights;
            static Counter()
            {
                Probe.Order = Probe.Order * 10 + 2;
                s_weights = [new() { Value = 1 }, new() { Value = 2 }, new() { Value = 3 }];
                System.GC.Collect();
            }
            public Counter() { Probe.Order = Probe.Order * 10 + 3; }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Sum(Payload extra, int rounds)
            {
                s_total = 0;
                for (var i = 0; i < rounds; i++) s_total += s_weights[i % s_weights.Length].Value + extra.Value;
                return s_total;
            }
        }
        public sealed class Derived : Counter
        {
            static Derived() { Probe.Order = Probe.Order * 10 + 1; }
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Probe.Order = 0;
                var extra = new Payload { Value = 1 };
                Counter counter = input == 0 ? new Counter() : new Derived();
                var order = input == 0 ? 23 : 123;
                return Probe.Order == order && counter.Sum(extra, 10) == 29 ? 42 : -1;
            }
            public static int Trace() => 0;
        }
        """, [0, 1])
    {
        ExpectedReturnValue = 42,
        RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
        Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
    });

    [Fact]
    public void AllocatingStaticInitializersPreserveLiveLocalsAndCallArguments() => Run(new(
        "StaticCallInitializationRoots", "NetWasm.Correctness.StaticCallInitializationRoots",
        """
        namespace NetWasm.Correctness.StaticCallInitializationRoots;

        public sealed class Payload { public int Value; }
        public static class Probe { public static int Collections; }
        public static class Direct
        {
            static Direct() { Probe.Collections++; System.GC.Collect(); }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Read(Payload value) => value.Value;
        }
        public static class Closed<T>
        {
            static Closed() { Probe.Collections++; System.GC.Collect(); }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Read(Payload value) => value.Value;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Probe.Collections = 0;
                var live = new Payload { Value = 19 };
                var argument = new Payload { Value = 23 };
                var result = input switch
                {
                    0 => Direct.Read(argument),
                    1 => Closed<int>.Read(argument),
                    _ => Closed<string>.Read(argument),
                };
                return Probe.Collections == 1 ? result + live.Value : -1;
            }
            public static int Trace() => 0;
        }
        """, [0, 1, 2])
    {
        ExpectedReturnValue = 42,
        RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
        Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
    });

    [Fact]
    public void StaticCallsInitializeTheirExactDeclaringTypeBeforeEnteringTheMethod() => Run(new(
        "StaticCallInitialization", "NetWasm.Correctness.StaticCallInitialization",
        """
        namespace NetWasm.Correctness.StaticCallInitialization;

        public static class Probe { public static int Count; }
        public static class Direct
        {
            static Direct() { Probe.Count++; }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Read() => Probe.Count;
        }
        public static class Closed<T>
        {
            static Closed() { Probe.Count++; }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Read() => Probe.Count;
        }
        public struct ValueOwner
        {
            static ValueOwner() { Probe.Count++; }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Read() => Probe.Count;
        }
        public static class Reentrant
        {
            static Reentrant() { Probe.Count++; Read(); }
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Read() => Probe.Count;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Probe.Count = 0;
                return input switch
                {
                    0 => Direct.Read() == 1 && Direct.Read() == 1 ? 42 : -1,
                    1 => Closed<int>.Read() == 1 && Closed<string>.Read() == 2 && Closed<int>.Read() == 2 ? 42 : -2,
                    2 => ValueOwner.Read() == 1 && ValueOwner.Read() == 1 ? 42 : -3,
                    _ => Reentrant.Read() == 1 && Reentrant.Read() == 1 ? 42 : -4,
                };
            }
            public static int Trace() => 0;
        }
        """, [0, 1, 2, 3])
    {
        ExpectedReturnValue = 42,
        ExecuteWasm64 = true,
        ExecuteOptimizedWasm = true,
    });

    public static TheoryData<string, string> Cells => CorpusCaseTestData.Cells(CorpusCaseTestData.StaticInitialization);

    [Theory]
    [MemberData(nameof(Cells))]
    public void InitializationPreservesClosedTypeStateOrderingReentryAndCachedFailure(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell);
}
