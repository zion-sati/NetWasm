namespace NetWasm.Compiler.Tests.Correctness;

public sealed class StaticInitializationFailureTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void FailureBoundariesPreserveFilterReentryNestedIdentityAndCollectedOriginals() => Run(new(
        "StaticInitializationFailureLifetime", "NetWasm.Correctness.StaticInitializationFailureLifetime",
        """
        using System;
        using System.Runtime.CompilerServices;
        namespace NetWasm.Correctness.StaticInitializationFailureLifetime;
        public sealed class Payload(int value) { public int Value = value; }
        public static class Probe
        {
            public static Exception? Original;
            public static int Attempts, Bodies, Filters, Order;
        }
        public static class Failure
        {
            static Failure()
            {
                Probe.Attempts++;
                try { throw Probe.Original!; }
                finally { Probe.Order = Probe.Order * 10 + 1; }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Read() { Probe.Bodies++; return -1; }
        }
        public static class Nested
        {
            static Nested() { Failure.Read(); }
            public static int Read() { Probe.Bodies++; return -1; }
        }
        public static class Ephemeral<T>
        {
            static Ephemeral() { Probe.Attempts++; throw new InvalidOperationException("ephemeral"); }
            public static int Read() { Probe.Bodies++; return -1; }
        }
        public static class EntryPoint
        {
            private static bool Desktop;
            public static int RunDesktop(int input) { Desktop = true; return Run(input); }
            // The selected NetWasm profile rethrows the exact original. Desktop
            // wraps each initializer boundary; normalize only its oracle path.
            private static Exception Original(Exception failure) =>
                Desktop ? ((TypeInitializationException)failure).InnerException! : failure;
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Filter(Exception failure)
            {
                Probe.Filters++;
                if (!ReferenceEquals(Original(failure), Probe.Original)) return false;
                Probe.Order = Probe.Order * 10 + 2;
                GC.Collect();
                try { Failure.Read(); return false; }
                catch (Exception repeated)
                {
                    return ReferenceEquals(Original(repeated), Probe.Original);
                }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CatchFailure()
            {
                try { Failure.Read(); return -1; }
                catch (Exception failure) when (Filter(failure)) { return 23; }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static WeakReference Capture<T>()
            {
                try { Ephemeral<T>.Read(); return new WeakReference(null); }
                catch (Exception failure) { return new WeakReference(Original(failure)); }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Exception CaptureNested()
            {
                try { Nested.Read(); return new Exception(); }
                catch (Exception failure) { return Desktop ? Original(Original(failure)) : failure; }
            }
            public static int Run(int input)
            {
                // Precreate the exception outside Failure's allocation-free body.
                // Outside filters may allocate even when the initializer does not.
                Probe.Original = input == 3
                    ? new TypeInitializationException("explicit", new InvalidOperationException("original"))
                    : new InvalidOperationException("original");
                Probe.Attempts = Probe.Bodies = Probe.Filters = Probe.Order = 0;
                if (input == 0 || input == 3)
                {
                    var live = new Payload(19);
                    var result = CatchFailure();
                    return live.Value + result == 42 && Probe.Attempts == 1 && Probe.Bodies == 0 &&
                        Probe.Filters == 1 && Probe.Order == 12 ? 42 : -10;
                }
                if (input == 1)
                {
                    var nested = CaptureNested();
                    GC.Collect();
                    if (!ReferenceEquals(nested, Probe.Original)) return -11;
                    return ReferenceEquals(CaptureNested(), nested) && Probe.Attempts == 1 && Probe.Bodies == 0 ? 42 : -12;
                }
                var first = Capture<int>();
                var second = Capture<string>();
                GC.Collect();
                var firstOriginal = first.Target;
                var secondOriginal = second.Target;
                if (firstOriginal is null || secondOriginal is null || ReferenceEquals(firstOriginal, secondOriginal)) return -13;
                if (!ReferenceEquals(Capture<int>().Target, firstOriginal) ||
                    !ReferenceEquals(Capture<string>().Target, secondOriginal)) return -14;
                return Probe.Attempts == 2 && Probe.Bodies == 0 ? 42 : -15;
            }
            public static int Trace() => 0;
        }
        """, [0, 1, 2, 3])
    {
        ExpectedReturnValue = 42,
        DesktopEntryMethod = "RunDesktop",
        SameSourceReason = "NetWasm caches the original exception; the desktop entry unwraps its runtime-created initializer wrappers.",
        RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection | OracleRuntimeCapabilities.WeakReferenceClearing,
        Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
    });

    [Fact]
    public void IndependentFailuresSurviveStrongHandleTableGrowthAndCollection()
    {
        const int count = 80;
        var markers = string.Join("\n", Enumerable.Range(0, count).Select(index => $"public struct Marker{index} {{ }}"));
        var calls = string.Join("\n", Enumerable.Range(0, count).Select(index => $"if (!Check<Marker{index}>({index})) return -{index + 1};"));
        Run(new("StaticInitializationFailureHandleGrowth", "NetWasm.Correctness.StaticInitializationFailureHandleGrowth",
            $$"""
            using System;
            namespace NetWasm.Correctness.StaticInitializationFailureHandleGrowth;
            public static class Probe
            {
                public static int Attempts, Bodies;
                public static WeakReference[] Originals = new WeakReference[{{count}}];
            }
            public static class Failure<T>
            {
                static Failure() { Probe.Attempts++; throw new InvalidOperationException(); }
                public static int Read() { Probe.Bodies++; return 0; }
            }
            {{markers}}
            public static class EntryPoint
            {
                private static bool Desktop;
                public static int RunDesktop(int input) { Desktop = true; return Run(input); }
                private static bool Check<T>(int index)
                {
                    try { Failure<T>.Read(); return false; }
                    catch (Exception failure)
                    {
                        var original = Desktop ? ((TypeInitializationException)failure).InnerException! : failure;
                        if (original is not InvalidOperationException) return false;
                        if (Probe.Originals[index] is null)
                            Probe.Originals[index] = new WeakReference(original);
                        else if (!ReferenceEquals(Probe.Originals[index].Target, original))
                            return false;
                        return true;
                    }
                }
                public static int Run(int input)
                {
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        {{calls}}
                        GC.Collect();
                    }
                    return Probe.Attempts == {{count}} && Probe.Bodies == 0 ? 42 : -100;
                }
                public static int Trace() => 0;
            }
            """, [0])
        {
            ExpectedReturnValue = 42,
            DesktopEntryMethod = "RunDesktop",
            SameSourceReason = "NetWasm caches the original exception; the desktop entry unwraps its runtime-created initializer wrapper.",
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection | OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });
    }
}
