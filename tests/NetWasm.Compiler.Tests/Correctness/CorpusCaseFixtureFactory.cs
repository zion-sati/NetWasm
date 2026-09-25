using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCaseFixtureFactory
{
    CorpusFixture Create(CorpusCaseManifest manifest, string cell, int? input = null);
}

internal sealed class CorpusCaseFixtureFactory(
    ICorpusCaseManifestVerifier verifier,
    ICorpusMatrixExpander matrices,
    IOracleModePolicyRegistry oracleModes) : ICorpusCaseFixtureFactory
{
    public CorpusFixture Create(CorpusCaseManifest manifest, string cell, int? input = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(cell);
        verifier.Verify(manifest);
        var selection = new CorpusMatrixSelection(manifest.MatrixProfile, cell, manifest.ExecutionBackend);
        matrices.Expand(manifest.InputKind, selection);
        if (input is { } selected && !manifest.Inputs.Contains(selected))
        {
            throw new ArgumentException("Selected input is outside the declared case.", nameof(input));
        }
        var inputs = input is { } value ? [value] : manifest.Inputs;
        var expectations = manifest.Expectations.Where(item => inputs.Contains(item.Input)).ToImmutableArray();
        var fixture = new CorpusFixture(manifest.Name, manifest.Namespace, string.Empty, inputs)
        {
            CaseId = manifest.CaseId,
            FeatureIds = manifest.FeatureIds,
            ReplayTestMethod = manifest.TestMethod,
            ReplayInput = input,
            Matrix = selection,
            AllowUnsafe = manifest.AllowUnsafe,
            RequiresReactor = manifest.RequiresReactor,
            RequiredRuntimeCapabilities = manifest.RequiredRuntimeCapabilities,
            ExpectedReturnValues = expectations.Where(item => item.ReturnValue.HasValue)
                .ToImmutableDictionary(item => item.Input, item => item.ReturnValue!.Value),
            ExpectedExceptionTypes = expectations.Where(item => item.ExceptionType is not null)
                .ToImmutableDictionary(item => item.Input, item => item.ExceptionType!),
            UsesTypedTrace = manifest.UsesTypedTrace,
            ExposesLegacyTrace = manifest.ExposesLegacyTrace,
            SupportsBatchedOracle = manifest.SupportsBatchedOracle,
            ReportAllMismatches = manifest.ReportAllMismatches,
            DesktopEntryMethod = manifest.DesktopEntryMethod ?? manifest.EntryMethod,
            WasmEntryMethod = manifest.EntryMethod,
            NetWasmReferencePaths = manifest.ReferencePaths,
            ReferenceAssemblyAliases = manifest.ReferenceAssemblyAliases,
            OracleMode = manifest.OracleMode,
            SameSourceReason = manifest.SameSourceReason,
        };
        oracleModes.Get(fixture.OracleMode).Validate(fixture);
        return fixture;
    }
}
