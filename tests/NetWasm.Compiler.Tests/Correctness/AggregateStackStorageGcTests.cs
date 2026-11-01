namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AggregateStackStorageGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void PreservedAggregateAndManagedPointeeStorageSurviveRealCollection() =>
        runner.Run(new("AggregateStackStorageRoots", "AggregateStackStorageRoots", Source, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14])
        {
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    [Fact]
    public void ProjectedByrefLocalsRetainTheirOwnerBeforeAndAfterReload() =>
        runner.Run(new("AggregateStackStorageLocalRoots", "AggregateStackStorageRoots", Source, [6, 7])
        {
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, "Debug-Wasm32-Direct-Linked",
                CorpusExecutionBackend.Linked),
        });

    [Fact]
    public void ProjectedByrefLocalsRetainTheirOwnerAcrossFilterAndFinallyContinuations() =>
        runner.Run(new("AggregateStackStorageExceptionRoots", "AggregateStackStorageRoots", Source, [8, 9])
        {
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, "Debug-Wasm32-Direct-Linked",
                CorpusExecutionBackend.Linked),
        });

    [Fact]
    public void ProjectedByrefLocalsSurviveFilterOutcomesAndNestedUnwind() =>
        runner.Run(new("AggregateStackStorageFilterRoots", "AggregateStackStorageRoots", Source, [10, 11, 12, 13])
        {
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, "Debug-Wasm32-Direct-Linked",
                CorpusExecutionBackend.Linked),
        });

    [Fact]
    public void CapturedByrefLocalAnchorSurvivesFilterCollection() =>
        runner.Run(new("AggregateStackStorageCapturedFilterRoot", "AggregateStackStorageRoots", Source, [14])
        {
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, "Debug-Wasm32-Direct-Linked",
                CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace AggregateStackStorageRoots;

        public sealed class Payload
        {
            public int Value;
        }
        public struct Envelope<T>
        {
            public T Payload;
            public long Tag;
        }
        public unsafe struct BufferOwner
        {
            public fixed int Values[8];
            public Payload Payload;
        }
        public struct ScalarOwner
        {
            public int Number;
            public Payload Payload;
        }
        public static class EntryPoint
        {
            private static WeakReference<Payload> _weak = null!;
            private static int _filterCollected;
            public static int Trace() => 0;

            public static unsafe int Run(int input)
            {
                if (input == 0) return Consume(Create(), Collect());
                if (input == 4) return ConsumeDerivedOwner();
                if (input == 5) return ConsumeLiveOwner();
                if (input == 6) return ConsumeProjectedLocal(beforeReload: false);
                if (input == 7) return ConsumeProjectedLocal(beforeReload: true);
                if (input == 8) return ConsumeFilteredLocal();
                if (input == 9) return ConsumeFinallyLocal();
                if (input == 10) return ConsumeFilterOutcome(1);
                if (input == 11) return ConsumeFilterOutcome(0);
                if (input == 12) return ConsumeFilterOutcome(2);
                if (input == 13) return ConsumeNestedFinally();
                if (input == 14) return ConsumeCapturedFilterLocal();
                var value = Create();
                if (input == 1) return ConsumeReference(ref value, Collect());
                if (input == 2) return ConsumeReference(ref Identity(ref value), Collect());
                var payload = value.Payload;
                value = default;
                return ConsumePayload(ref payload, Collect());
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Envelope<Payload> Create()
            {
                var payload = new Payload { Value = 42 };
                _weak = new WeakReference<Payload>(payload);
                return new Envelope<Payload> { Payload = payload, Tag = 7 };
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Collect()
            {
                var before = GC.CollectionCount(0);
                GC.Collect();
                return GC.CollectionCount(0) > before ? 1 : 0;
            }

            private static ref Envelope<T> Identity<T>(ref Envelope<T> value) => ref value;

            private static int Consume(Envelope<Payload> value, int collected) =>
                _weak.TryGetTarget(out var target) && ReferenceEquals(target, value.Payload) &&
                target.Value == 42 && value.Tag == 7 && collected == 1 ? 42 : 1;

            private static int ConsumeReference(ref Envelope<Payload> value, int collected) => Consume(value, collected);

            private static int ConsumePayload(ref Payload value, int collected) =>
                _weak.TryGetTarget(out var target) && ReferenceEquals(target, value) &&
                target.Value == 42 && collected == 1 ? 42 : 1;

            private static int ConsumeBuffer(ref int value, int collected) =>
                _weak.TryGetTarget(out var target) && target.Value == 42 &&
                value == 42 && collected == 1 ? 42 : 1;

            private static unsafe int ConsumeDerivedOwner()
            {
                BufferOwner buffer = default;
                buffer.Payload = Create().Payload;
                buffer.Values[1] = 42;
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<BufferOwner, byte>(ref buffer),
                    ref Unsafe.As<int, byte>(ref buffer.Values[1]));
                return ConsumeOwnerPayload(ref buffer.Values[1], displacement, Collect());
            }

            private static int ConsumeOwnerPayload(ref int value, nint displacement, int collected)
            {
                ref var start = ref Unsafe.SubtractByteOffset(ref Unsafe.As<int, byte>(ref value), displacement);
                ref var owner = ref Unsafe.As<byte, BufferOwner>(ref start);
                return value == 42 ? ConsumePayload(ref owner.Payload, collected) : 1;
            }

            private static unsafe int ConsumeProjectedLocal(bool beforeReload)
            {
                BufferOwner buffer = default;
                buffer.Payload = Create().Payload;
                buffer.Values[1] = 42;
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<BufferOwner, byte>(ref buffer),
                    ref Unsafe.As<int, byte>(ref buffer.Values[1]));
                ref var value = ref buffer.Values[1];
                if (beforeReload)
                {
                    var collected = Collect();
                    return ConsumeOwnerPayload(ref value, displacement, collected);
                }
                return ConsumeOwnerPayload(ref value, displacement, Collect());
            }

            private static int ConsumeFilteredLocal()
            {
                var owner = new ScalarOwner { Number = 42, Payload = Create().Payload };
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<ScalarOwner, byte>(ref owner),
                    ref Unsafe.As<int, byte>(ref owner.Number));
                ref var value = ref owner.Number;
                try
                {
                    Throw();
                }
                catch (Exception) when (Accept())
                {
                    return ConsumeScalarOwnerPayload(ref value, displacement, Collect());
                }
                return 1;
            }

            private static int ConsumeFinallyLocal()
            {
                ScalarOwner first = default;
                var second = new ScalarOwner { Number = 42, Payload = Create().Payload };
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<ScalarOwner, byte>(ref second),
                    ref Unsafe.As<int, byte>(ref second.Number));
                ref var value = ref first.Number;
                try
                {
                    Touch();
                }
                finally
                {
                    value = ref second.Number;
                }
                return ConsumeScalarOwnerPayload(ref value, displacement, Collect());
            }

            private static int ConsumeFilterOutcome(int mode)
            {
                var owner = new ScalarOwner { Number = 42, Payload = Create().Payload };
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<ScalarOwner, byte>(ref owner),
                    ref Unsafe.As<int, byte>(ref owner.Number));
                ref var value = ref owner.Number;
                try
                {
                    Throw();
                }
                catch (Exception) when (FilterDecision(mode))
                {
                    return mode == 1
                        ? ConsumeScalarOwnerPayload(ref value, displacement, _filterCollected)
                        : 1;
                }
                catch (Exception)
                {
                    return mode != 1
                        ? ConsumeScalarOwnerPayload(ref value, displacement, _filterCollected)
                        : 1;
                }
                return 1;
            }

            private static int ConsumeNestedFinally()
            {
                ScalarOwner first = default;
                var second = new ScalarOwner { Number = 42, Payload = Create().Payload };
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<ScalarOwner, byte>(ref second),
                    ref Unsafe.As<int, byte>(ref second.Number));
                ref var value = ref first.Number;
                try
                {
                    try
                    {
                        Throw();
                    }
                    finally
                    {
                        value = ref second.Number;
                    }
                }
                catch (Exception) when (FilterDecision(1))
                {
                    return ConsumeScalarOwnerPayload(ref value, displacement, _filterCollected);
                }
                return 1;
            }

            private static ref ScalarOwner IdentityOwner(ref ScalarOwner owner) => ref owner;

            private static int ConsumeCapturedFilterLocal()
            {
                var owner = new ScalarOwner { Number = 42, Payload = Create().Payload };
                var displacement = Unsafe.ByteOffset(ref Unsafe.As<ScalarOwner, byte>(ref owner),
                    ref Unsafe.As<int, byte>(ref owner.Number));
                ref var value = ref IdentityOwner(ref owner).Number;
                try
                {
                    Throw();
                }
                catch (Exception) when (FilterDecision(1))
                {
                    return ConsumeScalarOwnerPayload(ref value, displacement, _filterCollected);
                }
                return 1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool FilterDecision(int mode) => FilterDecisionCore(mode);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool FilterDecisionCore(int mode)
            {
                var sentinel = new Payload { Value = 73 };
                _filterCollected = Collect();
                if (sentinel.Value != 73) return false;
                if (mode == 2) throw new Exception();
                return mode == 1;
            }

            private static int ConsumeScalarOwnerPayload(ref int value, nint displacement, int collected)
            {
                ref var start = ref Unsafe.SubtractByteOffset(ref Unsafe.As<int, byte>(ref value), displacement);
                ref var owner = ref Unsafe.As<byte, ScalarOwner>(ref start);
                return value == 42 ? ConsumePayload(ref owner.Payload, collected) : 1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Throw() => throw new Exception();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Accept() => true;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Touch() { }

            private static unsafe int ConsumeLiveOwner()
            {
                BufferOwner buffer = default;
                buffer.Payload = Create().Payload;
                buffer.Values[1] = 42;
                var result = ConsumeBuffer(ref buffer.Values[1], Collect());
                return buffer.Payload.Value == 42 ? result : 1;
            }
        }
        """;
}
