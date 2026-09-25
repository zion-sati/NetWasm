using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

// These inputs are retained in their manifests and in the frozen M8 evidence.
// Only the live desktop-equality oracle excludes them; the paired skipped
// theories make that policy visible in normal test discovery.
internal static class KnownNonBugSkipReasons
{
    public const string UncheckedRemainder =
        "Unchecked signed minimum % -1 may return zero or throw (C# 12.13.3-4; ECMA-335 III.3.55); desktop exception identity is not required.";

    public const string UnsignedSinglePrecision =
        "Unsigned 64-bit to Single may use intermediate precision (C# 10.2.3; ECMA-335 I.12.1.3); desktop exact bits are over-specific.";

    public const string NativeUnsignedSinglePrecision =
        "Wasm64 native-unsigned to Single may use intermediate precision (C# 10.2.3; ECMA-335 I.12.1.3); desktop exact bits are over-specific.";

    public const string LargeIntegralMathFRounding =
        "The desktop MathF.Round staged-scaling adjacent-bit result for these already-integral values is not a required API result.";

    public const string SpecialValueRoundingValidationOrder =
        "For NaN/infinity with invalid MidpointRounding, .NET 10 validation precedence is unspecified; .NET 11 validates the mode first.";

    public const string TrigonometricNegativeZero =
        "Math/MathF Sin and Tan do not promise desktop negative-zero result bits; exact C-runtime results may vary by architecture.";

    public const string PowFiniteExactBits =
        "Math.Pow finite results are architecture-dependent; the API does not promise desktop exact bits for these values.";

    public const string EmittedOpenDelegate =
        "Directly emitted open-instance delegate construction violates ECMA-335 II.14.6 and III.4.21 preconditions; desktop acceptance is not required.";

    public const string ZeroBoundRankOneArrayIdentity =
        "C# cannot express CLI T[*], and ECMA-335 does not require CoreCLR's zero-bound T[*]-to-T[] identity normalization or its derived mutable-Address exception.";

    public const string ComponentExportExceptionIdentity =
        "A WIT function returning only an integer cannot transport an arbitrary managed exception; the component adapter fails the operation, so desktop exception type identity is not observable across this ABI boundary.";
}

internal sealed record KnownNonBugCorpusPartition(
    CorpusCaseManifest ActiveManifest,
    ImmutableArray<int> SkippedInputs);

internal static class KnownNonBugCorpusPartitioner
{
    private sealed record Rule(ImmutableHashSet<int> Inputs, WasmTarget? Target = null);

    private static readonly ImmutableDictionary<string, Rule> Rules =
        new Dictionary<string, Rule>(StringComparer.Ordinal)
        {
            [CorpusCaseTestData.FixedIntegerArithmetic] = new([1377, 4864]),
            [CorpusCaseTestData.NativeArithmetic] = new([703]),
            [CorpusCaseTestData.UnsignedPrecision] = new([40, 41]),
            [CorpusCaseTestData.NativeFloating] = new(
                [728, 729, 732, 733, 844, 845, 848, 849, 960, 961, 964, 965], WasmTarget.Wasm64),
            [CorpusCaseTestData.FloatingRoundingDigits] = new(
                Enumerable.Range(1122, 6).Concat(Enumerable.Range(1164, 6)).ToImmutableHashSet()),
            [CorpusCaseTestData.RoundingArguments] = new(
                new[] { 152, 153, 155, 156, 158, 159, 161, 162,
                    197, 198, 200, 201, 203, 204, 206, 207,
                    377, 378, 380, 381, 383, 384, 386, 387,
                    422, 423, 425, 426, 428, 429, 431, 432 }.ToImmutableHashSet()),
            [CorpusCaseTestData.FloatingMathDomains] = new([26, 42, 206, 222]),
            [CorpusCaseTestData.BinaryMathEdges] = new(
                [392, 394, 395, 408, 410, 411, 425, 426, 427, 441, 442, 443]),
            [CorpusCaseTestData.RankOne] = new([1, 2]),
            [CorpusCaseTestData.AsyncConversionTransport] = new(
                Enumerable.Range(32, 4)
                    .Concat(Enumerable.Range(40, 4))
                    .Concat(Enumerable.Range(48, 4))
                    .Concat(Enumerable.Range(224, 4))
                    .Concat(Enumerable.Range(232, 4))
                    .Concat(Enumerable.Range(240, 4))
                    .Concat(Enumerable.Range(352, 24))
                    .Concat(Enumerable.Range(704, 24))
                    .ToImmutableHashSet()),
            [CorpusCaseTestData.ImplicitExceptionLifetime] = new(
                Enumerable.Range(16, 14).ToImmutableHashSet()),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public static KnownNonBugCorpusPartition Partition(CorpusCaseManifest manifest, CorpusMatrixCell cell)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(cell);

        if (!Rules.TryGetValue(manifest.CaseId, out var rule) ||
            (rule.Target is { } target && target != cell.Target))
        {
            return new(manifest, []);
        }
        if (!rule.Inputs.IsSubsetOf(manifest.Inputs))
        {
            throw new InvalidOperationException("Known non-bug input IDs no longer match the case declaration.");
        }

        var activeInputs = manifest.Inputs.Where(input => !rule.Inputs.Contains(input)).ToImmutableArray();
        if (activeInputs.IsEmpty)
        {
            throw new InvalidOperationException("A known non-bug partition cannot disable an entire mixed case.");
        }
        var skippedInputs = manifest.Inputs.Where(rule.Inputs.Contains).ToImmutableArray();
        var activeExpectations = manifest.Expectations.Where(item => !rule.Inputs.Contains(item.Input))
            .ToImmutableArray();
        return new(manifest with { Inputs = activeInputs, Expectations = activeExpectations }, skippedInputs);
    }
}
