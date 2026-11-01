namespace NetWasm.Compiler.Tests.Correctness;

public sealed class UnsafeAccessorGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AccessorCallsPreserveCallerArgumentsResultsAndInteriorRoots() =>
        runner.Run(new("UnsafeAccessorRoots", "UnsafeAccessorRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.Finalization,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace UnsafeAccessorRoots;

        public static class Probe
        {
            public static int Finalized;
            public static void Collect()
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void MakeGarbage() => _ = new Witness(1);
        }
        public sealed class Witness
        {
            public int Value;
            public Witness(int value) => Value = value;
            ~Witness() => Probe.Finalized++;
        }
        public sealed class Owner
        {
            private Witness value;
            private static Witness? shared = null;
            public static int Initialized;
            static Owner()
            {
                Probe.Collect();
                Initialized = 42;
            }
            private Owner(Witness value)
            {
                this.value = value;
                Probe.Collect();
            }
            private Witness Return(Witness argument)
            {
                Probe.Collect();
                return argument;
            }
            private static Witness StaticReturn(Witness argument)
            {
                Probe.Collect();
                return argument;
            }
            private void Fail(Exception exception)
            {
                Probe.Collect();
                throw exception;
            }
            public Witness Value => value;
            public static Witness? Shared => shared;
            ~Owner() => Probe.Finalized++;
        }
        public sealed class GenericOwner<T> where T : class
        {
            private T Return(T argument)
            {
                Probe.Collect();
                return argument;
            }
        }
        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                Probe.MakeGarbage();
                Probe.Collect();
                if (Probe.Finalized != 1) return 1;
                Probe.Finalized = 0;

                var callerOnly = new Witness(10);
                var argument = new Witness(12);
                var owner = Construct(argument);
                if (Owner.Initialized != 42 || !ReferenceEquals(owner.Value, argument)) return 2;
                var methodArgument = new Witness(20);
                var result = Return(owner, methodArgument);
                result = StaticReturn(null!, result);
                result = GenericAccessors<Witness>.Return(new GenericOwner<Witness>(), result);
                if (!ReferenceEquals(result, methodArgument)) return 3;

                var failure = new InvalidOperationException("identity");
                try
                {
                    Fail(owner, failure);
                    return 4;
                }
                catch (InvalidOperationException actual)
                {
                    if (!ReferenceEquals(actual, failure)) return 5;
                }

                StaticField(null!) = result;
                if (!ReferenceEquals(Owner.Shared, result)) return 6;
                ref Witness interior = ref Field(owner);
                owner = null!;
                Probe.Collect();
                if (Probe.Finalized != 0) return 7;
                return callerOnly.Value + interior.Value + result.Value;
            }
            [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
            private static extern Owner Construct(Witness value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Return")]
            private static extern Witness Return(Owner owner, Witness argument);
            [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "StaticReturn")]
            private static extern Witness StaticReturn(Owner owner, Witness argument);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Fail")]
            private static extern void Fail(Owner owner, Exception exception);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "value")]
            private static extern ref Witness Field(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "shared")]
            private static extern ref Witness? StaticField(Owner owner);
            private static class GenericAccessors<T> where T : class
            {
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Return")]
                public static extern T Return(GenericOwner<T> owner, T argument);
            }
        }
        """;
}
