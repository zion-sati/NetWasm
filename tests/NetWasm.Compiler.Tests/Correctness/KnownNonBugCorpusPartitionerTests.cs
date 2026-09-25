using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class KnownNonBugCorpusPartitionerTests
{
    private static readonly ImmutableDictionary<string, ImmutableArray<int>> ExpectedSkippedInputs =
        new Dictionary<string, ImmutableArray<int>>(StringComparer.Ordinal)
        {
            [CorpusCaseTestData.FixedIntegerArithmetic] = [1377, 4864],
            [CorpusCaseTestData.NativeArithmetic] = [703],
            [CorpusCaseTestData.UnsignedPrecision] = [40, 41],
            [CorpusCaseTestData.NativeFloating] =
                [728, 729, 732, 733, 844, 845, 848, 849, 960, 961, 964, 965],
            [CorpusCaseTestData.FloatingRoundingDigits] =
                [1122, 1123, 1124, 1125, 1126, 1127, 1164, 1165, 1166, 1167, 1168, 1169],
            [CorpusCaseTestData.RoundingArguments] =
                [152, 153, 155, 156, 158, 159, 161, 162,
                    197, 198, 200, 201, 203, 204, 206, 207,
                    377, 378, 380, 381, 383, 384, 386, 387,
                    422, 423, 425, 426, 428, 429, 431, 432],
            [CorpusCaseTestData.FloatingMathDomains] = [26, 42, 206, 222],
            [CorpusCaseTestData.BinaryMathEdges] =
                [392, 394, 395, 408, 410, 411, 425, 426, 427, 441, 442, 443],
            [CorpusCaseTestData.RankOne] = [1, 2],
            [CorpusCaseTestData.AsyncConversionTransport] =
                [32, 33, 34, 35, 40, 41, 42, 43, 48, 49, 50, 51,
                    224, 225, 226, 227, 232, 233, 234, 235, 240, 241, 242, 243,
                    352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363,
                    364, 365, 366, 367, 368, 369, 370, 371, 372, 373, 374, 375,
                    704, 705, 706, 707, 708, 709, 710, 711, 712, 713, 714, 715,
                    716, 717, 718, 719, 720, 721, 722, 723, 724, 725, 726, 727],
            [CorpusCaseTestData.ImplicitExceptionLifetime] =
                [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29],
        }.ToImmutableDictionary(StringComparer.Ordinal);

    [Fact]
    public void KnownDifferencesExcludeOnlyDeclaredInputsForApplicableTargets()
    {
        var verifier = new CorpusCaseManifestVerifier(CorpusCaseTestData.Assets.Features,
            new CorpusSourceNamesVerifier(), new CorpusMatrixExpander(), new CorpusReplayCommandFormatter());
        foreach (var (caseId, expectedIds) in ExpectedSkippedInputs)
        {
            var manifest = CorpusCaseTestData.Get(caseId);
            var cells = new CorpusMatrixExpander().Expand(manifest.InputKind,
                new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend));
            foreach (var cell in cells)
            {
                var partition = KnownNonBugCorpusPartitioner.Partition(manifest, cell);
                var applicable = caseId != CorpusCaseTestData.NativeFloating || cell.Target == WasmTarget.Wasm64;
                var skipped = applicable ? expectedIds : [];
                Assert.Equal(skipped.Order(), partition.SkippedInputs.Order());
                Assert.Equal(manifest.Inputs.Where(input => !skipped.Contains(input)), partition.ActiveManifest.Inputs);
                Assert.Equal(manifest.Expectations.Where(item => !skipped.Contains(item.Input)),
                    partition.ActiveManifest.Expectations);
                Assert.Equal(manifest.Inputs.Length,
                    partition.ActiveManifest.Inputs.Length + partition.SkippedInputs.Length);
                verifier.Verify(partition.ActiveManifest);
                var composed = CorpusCaseTestData.Partition(caseId, cell.Id);
                Assert.Equal(partition.SkippedInputs.AsEnumerable(), composed.SkippedInputs.AsEnumerable());
                Assert.Equal(partition.ActiveManifest.Inputs.AsEnumerable(), composed.ActiveManifest.Inputs.AsEnumerable());
                Assert.Equal(partition.ActiveManifest.Expectations.AsEnumerable(),
                    composed.ActiveManifest.Expectations.AsEnumerable());
            }
        }
    }

    [Fact]
    public void UnlistedCaseAndWasm32NativeControlsRemainUnchanged()
    {
        var unchanged = CorpusCaseTestData.Get(CorpusCaseTestData.Math);
        var cell = new CorpusMatrixExpander().Expand(unchanged.InputKind,
            new(unchanged.MatrixProfile, Backend: unchanged.ExecutionBackend))[0];
        var result = KnownNonBugCorpusPartitioner.Partition(unchanged, cell);
        Assert.Same(unchanged, result.ActiveManifest);
        Assert.Empty(result.SkippedInputs);

        var native = CorpusCaseTestData.Get(CorpusCaseTestData.NativeFloating);
        var wasm32 = new CorpusMatrixExpander().Expand(native.InputKind,
            new(native.MatrixProfile, Backend: native.ExecutionBackend))
            .First(candidate => candidate.Target == WasmTarget.Wasm32);
        result = KnownNonBugCorpusPartitioner.Partition(native, wasm32);
        Assert.Same(native, result.ActiveManifest);
        Assert.Empty(result.SkippedInputs);
    }

    [Fact]
    public void StaleOrWholeCaseRuleCannotHideARegression()
    {
        var manifest = CorpusCaseTestData.Get(CorpusCaseTestData.FixedIntegerArithmetic);
        var cell = new CorpusMatrixExpander().Expand(manifest.InputKind,
            new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend))[0];
        Assert.Throws<InvalidOperationException>(() => KnownNonBugCorpusPartitioner.Partition(
            manifest with { Inputs = manifest.Inputs.Where(input => input != 1377).ToImmutableArray() }, cell));
        Assert.Throws<InvalidOperationException>(() => KnownNonBugCorpusPartitioner.Partition(
            manifest with { Inputs = [1377, 4864], Expectations = [] }, cell));
    }

    [Fact]
    public void MissingPartitionArgumentsAreRejected()
    {
        var manifest = CorpusCaseTestData.Get(CorpusCaseTestData.FixedIntegerArithmetic);
        var cell = new CorpusMatrixExpander().Expand(manifest.InputKind,
            new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend))[0];
        Assert.Throws<ArgumentNullException>(() => KnownNonBugCorpusPartitioner.Partition(null!, cell));
        Assert.Throws<ArgumentNullException>(() => KnownNonBugCorpusPartitioner.Partition(manifest, null!));
    }

    [Fact]
    public void EveryKnownDifferenceHasAReasonBearingSkipAttribute()
    {
        var markers = new (Type Owner, string Method, string Reason)[]
        {
            (typeof(IntegerArithmeticEdgeTests),
                nameof(IntegerArithmeticEdgeTests.FixedUncheckedMinimumRemainderDesktopChoiceIsNotRequired),
                KnownNonBugSkipReasons.UncheckedRemainder),
            (typeof(IntegerArithmeticEdgeTests),
                nameof(IntegerArithmeticEdgeTests.NativeUncheckedMinimumRemainderDesktopChoiceIsNotRequired),
                KnownNonBugSkipReasons.UncheckedRemainder),
            (typeof(NumericConversionEdgeTests),
                nameof(NumericConversionEdgeTests.UnsignedToSingleDesktopExactBitsAreNotRequired),
                KnownNonBugSkipReasons.UnsignedSinglePrecision),
            (typeof(NumericConversionEdgeTests),
                nameof(NumericConversionEdgeTests.NativeUnsignedToSingleDesktopExactBitsAreNotRequired),
                KnownNonBugSkipReasons.NativeUnsignedSinglePrecision),
            (typeof(FloatingMathEdgeTests),
                nameof(FloatingMathEdgeTests.LargeIntegralMathFRoundingDesktopScalingArtifactIsNotRequired),
                KnownNonBugSkipReasons.LargeIntegralMathFRounding),
            (typeof(FloatingMathEdgeTests),
                nameof(FloatingMathEdgeTests.SpecialValueRoundingInvalidModePrecedenceIsNotRequired),
                KnownNonBugSkipReasons.SpecialValueRoundingValidationOrder),
            (typeof(FloatingMathEdgeTests),
                nameof(FloatingMathEdgeTests.TrigonometricNegativeZeroDesktopBitsAreNotRequired),
                KnownNonBugSkipReasons.TrigonometricNegativeZero),
            (typeof(FloatingMathEdgeTests),
                nameof(FloatingMathEdgeTests.PowFiniteDesktopExactBitsAreNotRequired),
                KnownNonBugSkipReasons.PowFiniteExactBits),
            (typeof(RttiArrayOperationCompilationTests),
                nameof(RttiArrayOperationCompilationTests.CoreClrZeroBoundRankOneNormalizationIsNotRequired),
                KnownNonBugSkipReasons.ZeroBoundRankOneArrayIdentity),
            (typeof(NumericTransportEdgeTests),
                nameof(NumericTransportEdgeTests.ComponentExportsCannotExposeArbitraryManagedExceptionIdentity),
                KnownNonBugSkipReasons.ComponentExportExceptionIdentity),
            (typeof(AsyncLifetimeTests),
                nameof(AsyncLifetimeTests.EscapingComponentExceptionsDoNotPromiseDesktopTypeIdentity),
                KnownNonBugSkipReasons.ComponentExportExceptionIdentity),
        };

        Assert.Equal(ExpectedSkippedInputs.Count, markers.Length);
        foreach (var (owner, name, reason) in markers)
        {
            var method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.Equal(reason, Assert.IsType<FactAttribute>(method.GetCustomAttribute<FactAttribute>()).Skip);
        }

        var zeroBound = typeof(RttiArrayOperationCompilationTests).GetMethod(
            nameof(RttiArrayOperationCompilationTests.CompilerPreservesZeroBasedRankOneArrayIdentity),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(zeroBound);
        Assert.Equal(KnownNonBugSkipReasons.ZeroBoundRankOneArrayIdentity,
            Assert.IsType<TheoryAttribute>(zeroBound.GetCustomAttribute<TheoryAttribute>()).Skip);
    }
}
