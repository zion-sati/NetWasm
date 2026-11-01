using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class DelegateCompatibilityTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DelegateSemanticsMatchDesktopAcrossProfilesTargetsAndWasmForms()
    {
        runner.Run(CreateFixture().WithFullExecutionMatrix());
    }

    [Fact]
    public void DelegateTargetsSurviveCollectionInTheLinkedRuntime()
    {
        runner.Run(CreateFixture([9]) with
        {
            Matrix = new(
                CorpusMatrixProfile.Extended,
                "Release-Wasm32-Direct-Linked",
                CorpusExecutionBackend.Linked),
        });
    }

    private static CorpusFixture CreateFixture(
        ImmutableArray<int> inputs = default)
    {
        inputs = inputs.IsDefault ? [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10] : inputs;
        return new(
            "DelegateCompatibility",
            "NetWasm.Correctness.Delegates",
            Source,
            inputs)
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            ExpectedReturnValues = inputs.ToImmutableDictionary(
                input => input,
                input => 500 + input),
            SameSourceReason =
                "Delegate combination, removal, identity, invocation and cloning are ordinary CLR contracts.",
        };
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.Delegates;

        public delegate void Handler();
        public delegate void OtherHandler();
        public delegate void CountHandler();
        public delegate int RefHandler(ref int value, out int observed);

        public static class EntryPoint
        {
            private static int _trace;
            private static int _count;

            public static int Run(int input) => input switch
            {
                0 => BinaryAndAssociatedCombination(),
                1 => ArrayAndSpanCombination(),
                2 => RemovalUsesTheLastContiguousSubsequence(),
                3 => TypeAndTargetIdentity(),
                4 => TargetCloneAndInvocationList(),
                5 => InvocationListEnumeration(),
                6 => LongFlatInvocationList(),
                7 => ByReferenceInvocation(),
                8 => ExceptionStopsTheSequence(),
                9 => TargetsSurviveCollection(),
                _ => EqualityAndHashingAgreeInDictionary(),
            };

            public static int Trace() => _trace;

            private static int BinaryAndAssociatedCombination()
            {
                Handler a = A;
                Handler b = B;
                Handler c = C;
                Handler left = (a + b) + c;
                Handler right = a + (b + c);
                _trace = 0;
                left();
                return Bool(
                    _trace == 123 &&
                    left == right &&
                    left.Equals(right) &&
                    left.GetHashCode() == right.GetHashCode(),
                    500);
            }

            private static int ArrayAndSpanCombination()
            {
                Handler a = A;
                Handler b = B;
                Handler c = C;
                Delegate?[] values = [null, a, null, b, c, null];
                var fromArray = (Handler)Delegate.Combine(values)!;
                ReadOnlySpan<Delegate?> span = values;
                var fromSpan = (Handler)Delegate.Combine(span)!;

                _trace = 0;
                fromArray();
                var arrayTrace = _trace;
                _trace = 0;
                fromSpan();
                return Bool(
                    arrayTrace == 123 &&
                    _trace == 123 &&
                    fromArray == fromSpan,
                    501);
            }

            private static int RemovalUsesTheLastContiguousSubsequence()
            {
                Handler a = A;
                Handler b = B;
                Handler c = C;
                Handler source = a + b + a + b + c;
                Handler value = a + b;
                var removed = (Handler)Delegate.Remove(source, value)!;
                _trace = 0;
                removed();
                if (_trace != 123)
                {
                    return -502;
                }

                Handler overlap = a + a + a;
                var overlapResult = (Handler)Delegate.Remove(overlap, a + a)!;
                if (!ReferenceEquals(overlapResult, a))
                {
                    return -502;
                }

                Handler pair = a + b;
                if (!ReferenceEquals(Delegate.Remove(pair, a), b) ||
                    Delegate.Remove(pair, pair) is not null ||
                    !ReferenceEquals(Delegate.Remove(source, c + c), source) ||
                    Delegate.RemoveAll(a + b + a + b, a + b) is not null)
                {
                    return -502;
                }
                return 502;
            }

            private static int TypeAndTargetIdentity()
            {
                var firstTarget = new AlwaysEqualTarget();
                var secondTarget = new AlwaysEqualTarget();
                Handler first = firstTarget.Invoke;
                Handler sameTarget = firstTarget.Invoke;
                Handler second = secondTarget.Invoke;
                OtherHandler other = firstTarget.Invoke;
                var mismatchCount = 0;
                try
                {
                    _ = Delegate.Combine(first, other);
                }
                catch (ArgumentException)
                {
                    mismatchCount++;
                }
                try
                {
                    _ = Delegate.Remove(first, other);
                }
                catch (ArgumentException)
                {
                    mismatchCount++;
                }

                return Bool(
                    first == sameTarget &&
                    first.GetHashCode() == sameTarget.GetHashCode() &&
                    first != second &&
                    !first.Equals(other) &&
                    mismatchCount == 2,
                    503);
            }

            private static int TargetCloneAndInvocationList()
            {
                var firstTarget = new AlwaysEqualTarget();
                var secondTarget = new AlwaysEqualTarget();
                Handler first = firstTarget.Invoke;
                Handler second = secondTarget.Invoke;
                Handler combined = first + second;
                var clone = (Handler)combined.Clone();
                var interfaceClone = (Handler)((ICloneable)combined).Clone();
                var invocationList = combined.GetInvocationList();

                _count = 0;
                clone();
                interfaceClone();
                Handler withStaticLast = combined + Increment;
                return Bool(
                    ReferenceEquals(combined.Target, secondTarget) &&
                    withStaticLast.Target is null &&
                    !ReferenceEquals(combined, clone) &&
                    !ReferenceEquals(combined, interfaceClone) &&
                    combined == clone &&
                    clone == interfaceClone &&
                    combined.GetHashCode() == clone.GetHashCode() &&
                    invocationList.Length == 2 &&
                    ReferenceEquals(invocationList[0], first) &&
                    ReferenceEquals(invocationList[1], second) &&
                    _count == 4,
                    504);
            }

            private static int InvocationListEnumeration()
            {
                var empty = default(Delegate.InvocationListEnumerator<Handler>);
                if (empty.Current is not null || empty.MoveNext())
                {
                    return -505;
                }
                var nullValue = Delegate.EnumerateInvocationList<Handler>(null);
                if (nullValue.Current is not null || nullValue.MoveNext())
                {
                    return -505;
                }

                Handler a = A;
                Handler b = B;
                Handler combined = a + b;
                var enumerator = Delegate.EnumerateInvocationList(combined);
                if (enumerator.Current is not null ||
                    !enumerator.MoveNext() ||
                    !ReferenceEquals(enumerator.Current, a) ||
                    !enumerator.MoveNext() ||
                    !ReferenceEquals(enumerator.Current, b) ||
                    enumerator.MoveNext() ||
                    enumerator.Current is not null)
                {
                    return -505;
                }
                return 505;
            }

            private static int LongFlatInvocationList()
            {
                CountHandler leaf = Increment;
                CountHandler chain = leaf;
                for (var index = 1; index < 4096; index++)
                {
                    chain += leaf;
                }
                _count = 0;
                chain();
                return Bool(
                    _count == 4096 &&
                    chain.GetInvocationList().Length == 4096,
                    506);
            }

            private static int ByReferenceInvocation()
            {
                RefHandler combined = RefA;
                combined += RefB;
                var value = 3;
                var result = combined(ref value, out var observed);
                return Bool(value == 8 && observed == 9 && result == 20, 507);
            }

            private static int ExceptionStopsTheSequence()
            {
                Handler combined = A;
                combined += Throwing;
                combined += C;
                _trace = 0;
                try
                {
                    combined();
                    return -508;
                }
                catch (InvalidOperationException)
                {
                    return Bool(_trace == 19, 508);
                }
            }

            private static int TargetsSurviveCollection()
            {
                var target = new CollectingTarget();
                Handler handler = target.Invoke;
                Handler combined = handler + handler + handler;
                combined();
                return Bool(
                    target.Count == 3 &&
                    ReferenceEquals(combined.Target, target),
                    509);
            }

            private static int EqualityAndHashingAgreeInDictionary()
            {
                var target = new AlwaysEqualTarget();
                Handler first = target.Invoke;
                Handler second = target.Invoke;
                Handler left = first + second;
                Handler right = first + second;
                var values = new Dictionary<Handler, int>();
                values[left] = 510;
                return Bool(
                    values.TryGetValue(right, out var value) && value == 510,
                    510);
            }

            private static void A() => _trace = _trace * 10 + 1;
            private static void B() => _trace = _trace * 10 + 2;
            private static void C() => _trace = _trace * 10 + 3;
            private static void Increment() => _count++;

            private static void Throwing()
            {
                _trace = _trace * 10 + 9;
                throw new InvalidOperationException();
            }

            private static int RefA(ref int value, out int observed)
            {
                value++;
                observed = value;
                return 10;
            }

            private static int RefB(ref int value, out int observed)
            {
                value *= 2;
                observed = value + 1;
                return 20;
            }

            private static int Bool(bool condition, int value) =>
                condition ? value : -value;

            private sealed class AlwaysEqualTarget
            {
                public void Invoke() => _count++;
                public override bool Equals(object? value) => value is AlwaysEqualTarget;
                public override int GetHashCode() => 1;
            }

            private sealed class CollectingTarget
            {
                public int Count;

                public void Invoke()
                {
                    GC.Collect();
                    Count++;
                }
            }
        }
        """;
}
