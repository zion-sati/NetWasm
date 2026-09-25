namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class StaticInitializationTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
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
