using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RuntimeHelpersCompilerContractsCompatibilityTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void RuntimeHelpersAndCompilerContractsMatchDesktopAcrossProfilesAndTargets()
    {
        runner.Run(CreateFixture().WithFullExecutionMatrix());
    }

    [Fact]
    public void RuntimeHelpersContractsMatchDesktopAgainstTheLinkedNativeRuntime()
    {
        runner.Run(CreateFixture() with
        {
            Matrix = new(
                CorpusMatrixProfile.Extended,
                "Release-Wasm32-Direct-Linked",
                CorpusExecutionBackend.Linked),
        });
    }

    private static CorpusFixture CreateFixture() =>
        new(
            "RuntimeHelpersCompilerContractsCompatibility",
            "NetWasm.Correctness.RuntimeHelpersCompilerContracts",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15])
        {
            CaptureCompilerDiagnostics = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            DesktopEntryMethod = "DesktopRun",
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty
                .Add(0, 400)
                .Add(1, 401)
                .Add(2, 402)
                .Add(3, 404)
                .Add(4, 405)
                .Add(5, 406)
                .Add(6, 407)
                .Add(7, 408)
                .Add(8, 409)
                .Add(9, 410)
                .Add(10, 411)
                .Add(11, 412)
                .Add(12, 413)
                .Add(13, 414)
                .Add(14, 415)
                .Add(15, 416),
            SameSourceReason =
                "RuntimeHelpers uses the desktop oracle; inputs 3 and 8 pin the selected .NET 11 and closed-world profiles.",
        };

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        #pragma warning disable SYSLIB0004

        namespace NetWasm.Correctness.RuntimeHelpersCompilerContracts;

        public static class EntryPoint
        {
            private static int state;

            public static int Run(int input) => input switch
            {
                0 => CovariantSlice(),
                1 => ExactSlice(),
                2 => CovariantEmptySlice(),
                3 => RuntimeFeaturesDescribeTheProfile(),
                4 => ReferenceContainmentMatchesTypeShape(),
                5 => GuaranteedCleanup(exceptional: false),
                6 => GuaranteedCleanup(exceptional: true),
                7 => FullClone(),
                8 => AttributeIdentitySemantics(),
                9 => PrepareDelegateAllowsNull(),
                10 => GetObjectValuePreservesIdentityWhereRequired(),
                11 => GetObjectValueCopiesMutableStructures(),
                12 => GetObjectValuePreservesEnumIdentity(),
                13 => GetObjectValueCopiesDecimal(),
                14 => GetObjectValueCopiesDateTime(),
                _ => RuntimeHelperEqualityIgnoresOverrides(),
            };

            public static int DesktopRun(int input) => input switch
            {
                3 => 404,
                8 => 409,
                _ => Run(input),
            };

            public static int Trace() => 0;

            private static int CovariantSlice()
            {
                object[] source = new string[] { "zero", "one", "two" };
                object[] slice = RuntimeHelpers.GetSubArray(source, 1..3);
                if (slice.GetType() != typeof(string[]))
                {
                    return -410;
                }
                if (slice.Length != 2)
                {
                    return -411;
                }
                if ((string)slice[0] != "one")
                {
                    return -412;
                }
                return (string)slice[1] == "two" ? 400 : -413;
            }

            private static int ExactSlice()
            {
                int[] source = [3, 5, 8];
                int[] slice = RuntimeHelpers.GetSubArray(source, 1..);
                return Bool(
                    slice.GetType() == typeof(int[]) &&
                    slice.Length == 2 &&
                    slice[0] == 5 &&
                    slice[1] == 8,
                    401);
            }

            private static int CovariantEmptySlice()
            {
                object[] source = new string[] { "value" };
                object[] slice = RuntimeHelpers.GetSubArray(source, 1..1);
                return Bool(slice.GetType() == typeof(string[]) && slice.Length == 0, 402);
            }

            private static int RuntimeFeaturesDescribeTheProfile()
            {
                bool supported =
                    RuntimeFeature.IsSupported(RuntimeFeature.PortablePdb) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.ByRefFields) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.ByRefLikeGenerics) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.CovariantReturnsOfClasses) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.DefaultImplementationsOfInterfaces) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.NumericIntPtr) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.UnmanagedSignatureCallingConvention) &&
                    RuntimeFeature.IsSupported(RuntimeFeature.VirtualStaticsInInterfaces) &&
                    !RuntimeFeature.IsDynamicCodeSupported &&
                    !RuntimeFeature.IsDynamicCodeCompiled &&
                    !RuntimeFeature.IsSupported(nameof(RuntimeFeature.IsDynamicCodeSupported)) &&
                    !RuntimeFeature.IsSupported(nameof(RuntimeFeature.IsDynamicCodeCompiled)) &&
                    !RuntimeFeature.IsSupported("UnknownFeature");
                return Bool(supported, 404);
            }

            private static int ReferenceContainmentMatchesTypeShape()
            {
                bool supported =
                    !RuntimeHelpers.IsReferenceOrContainsReferences<int>() &&
                    RuntimeHelpers.IsReferenceOrContainsReferences<string>() &&
                    RuntimeHelpers.IsReferenceOrContainsReferences<Pair>() &&
                    !RuntimeHelpers.IsReferenceOrContainsReferences<NumberPair>();
                return Bool(supported, 405);
            }

            private static int FullClone()
            {
                object[] source = new string[] { "left", "right" };
                object[] clone = (object[])source.Clone();
                return Bool(
                    clone.GetType() == typeof(string[]) &&
                    !ReferenceEquals(source, clone) &&
                    clone.Length == 2 &&
                    (string)clone[0] == "left" &&
                    (string)clone[1] == "right",
                    408);
            }

            private static int AttributeIdentitySemantics()
            {
                var first = new ProbeAttribute(17);
                var second = new ProbeAttribute(17);
                return Bool(
                    first.Equals(first) &&
                    !first.Equals(second) &&
                    first.GetHashCode() == RuntimeHelpers.GetHashCode(first),
                    409);
            }

            private static int PrepareDelegateAllowsNull()
            {
                RuntimeHelpers.PrepareDelegate(null!);
                return 410;
            }

            private static int GetObjectValuePreservesIdentityWhereRequired()
            {
                object reference = new object();
                object integer = 42;
                object nativeInteger = (nint)43;
                object nativeUnsignedInteger = (nuint)44;
                return Bool(
                    RuntimeHelpers.GetObjectValue(null) is null &&
                    ReferenceEquals(reference, RuntimeHelpers.GetObjectValue(reference)) &&
                    ReferenceEquals(integer, RuntimeHelpers.GetObjectValue(integer)) &&
                    ReferenceEquals(nativeInteger, RuntimeHelpers.GetObjectValue(nativeInteger)) &&
                    ReferenceEquals(
                        nativeUnsignedInteger,
                        RuntimeHelpers.GetObjectValue(nativeUnsignedInteger)),
                    411);
            }

            private static int GetObjectValueCopiesMutableStructures()
            {
                object shared = new object();
                object original = new MutableBox
                {
                    Prefix = 3,
                    Number = 17,
                    Reference = shared,
                };
                object copy = RuntimeHelpers.GetObjectValue(original);
                ref MutableBox originalValue = ref Unsafe.Unbox<MutableBox>(original);
                ref MutableBox copyValue = ref Unsafe.Unbox<MutableBox>(copy);
                originalValue.Number = 29;

                object genericOriginal = new GenericBox<int>
                {
                    Value = 31,
                    Reference = shared,
                };
                object genericCopy = RuntimeHelpers.GetObjectValue(genericOriginal);
                ref GenericBox<int> genericOriginalValue =
                    ref Unsafe.Unbox<GenericBox<int>>(genericOriginal);
                ref GenericBox<int> genericCopyValue =
                    ref Unsafe.Unbox<GenericBox<int>>(genericCopy);
                genericOriginalValue.Value = 37;

                return Bool(
                    !ReferenceEquals(original, copy) &&
                    copyValue.Prefix == 3 &&
                    copyValue.Number == 17 &&
                    ReferenceEquals(copyValue.Reference, shared) &&
                    !ReferenceEquals(genericOriginal, genericCopy) &&
                    genericCopyValue.Value == 31 &&
                    ReferenceEquals(genericCopyValue.Reference, shared),
                    412);
            }

            private static int GetObjectValuePreservesEnumIdentity()
            {
                object enumeration = ProbeEnum.Second;
                object enumerationCopy = RuntimeHelpers.GetObjectValue(enumeration);
                return Bool(
                    enumeration.GetType().IsEnum &&
                    ReferenceEquals(enumeration, enumerationCopy) &&
                    (ProbeEnum)enumerationCopy == ProbeEnum.Second,
                    413);
            }

            private static int GetObjectValueCopiesDecimal()
            {
                object decimalValue = 123.5m;
                object decimalCopy = RuntimeHelpers.GetObjectValue(decimalValue);
                return Bool(
                    !ReferenceEquals(decimalValue, decimalCopy) &&
                    (decimal)decimalCopy == 123.5m,
                    414);
            }

            private static int GetObjectValueCopiesDateTime()
            {
                object dateTime = new DateTime(638900000000000000L);
                object dateTimeCopy = RuntimeHelpers.GetObjectValue(dateTime);
                return Bool(
                    !ReferenceEquals(dateTime, dateTimeCopy) &&
                    ((DateTime)dateTimeCopy).Ticks == 638900000000000000L,
                    415);
            }

            private static int RuntimeHelperEqualityIgnoresOverrides()
            {
                var firstReference = new AlwaysEqual();
                var secondReference = new AlwaysEqual();
                object firstValue = new NeverEqual { Value = 41 };
                object secondValue = new NeverEqual { Value = 41 };
                object differentValue = new NeverEqual { Value = 43 };
                object differentType = 41;
                return Bool(
                    RuntimeHelpers.Equals(firstReference, firstReference) &&
                    !RuntimeHelpers.Equals(firstReference, secondReference) &&
                    RuntimeHelpers.Equals(null, null) &&
                    !RuntimeHelpers.Equals(firstReference, null) &&
                    !RuntimeHelpers.Equals(null, firstReference) &&
                    RuntimeHelpers.Equals(firstValue, secondValue) &&
                    !RuntimeHelpers.Equals(firstValue, differentValue) &&
                    !RuntimeHelpers.Equals(firstValue, differentType),
                    416);
            }

            private static int GuaranteedCleanup(bool exceptional)
            {
                state = 0;
                try
                {
                    RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(
                        RunCode,
                        CleanupCode,
                        exceptional);
                    return Bool(state == 1, 406);
                }
                catch (InvalidOperationException)
                {
                    return Bool(state == 2, 407);
                }
            }

            private static void RunCode(object? userData)
            {
                if ((bool)userData!)
                {
                    throw new InvalidOperationException();
                }
            }

            private static void CleanupCode(object? userData, bool exceptionThrown)
            {
                state = exceptionThrown ? 2 : 1;
            }

            private static int Bool(bool condition, int value) => condition ? value : -value;

            private struct Pair(string? text, int number)
            {
                public string? Text = text;
                public int Number = number;
            }

            private struct NumberPair(int first, long second)
            {
                public int First = first;
                public long Second = second;
            }

            private sealed class ProbeAttribute(int value) : Attribute
            {
                public int Value { get; } = value;
            }

            private struct MutableBox
            {
                public byte Prefix;
                public long Number;
                public object? Reference;
            }

            private struct GenericBox<T>
            {
                public T Value;
                public object? Reference;
            }

            private struct NeverEqual
            {
                public int Value;

                public override bool Equals(object? value) => false;
                public override int GetHashCode() => 0;
            }

            private sealed class AlwaysEqual
            {
                public override bool Equals(object? value) => true;
                public override int GetHashCode() => 0;
            }

            private enum ProbeEnum : short
            {
                First = 1,
                Second = 2,
            }
        }

        #pragma warning restore SYSLIB0004
        """;
}
