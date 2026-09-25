using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCaseManifestVerifier
{
    void Verify(CorpusCaseManifest manifest);
}

internal sealed partial class CorpusCaseManifestVerifier(
    CorpusFeatureCatalog features,
    ICorpusSourceNamesVerifier sourceNames,
    ICorpusMatrixExpander matrices,
    ICorpusReplayCommandFormatter replay) : ICorpusCaseManifestVerifier
{
    public void Verify(CorpusCaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported corpus manifest schema.", nameof(manifest));
        }
        if (manifest.CaseId is null || !CaseId().IsMatch(manifest.CaseId))
        {
            throw new ArgumentException("A stable lowercase case ID is required.", nameof(manifest));
        }
        if (manifest.FeatureIds.IsDefaultOrEmpty ||
            manifest.FeatureIds.Distinct(StringComparer.Ordinal).Count() != manifest.FeatureIds.Length ||
            manifest.FeatureIds.Any(id => id is null || !features.Ids.Contains(id)))
        {
            throw new ArgumentException("Feature IDs must be distinct members of the feature catalog.", nameof(manifest));
        }
        if (manifest.Name is null || !Identifier().IsMatch(manifest.Name) ||
            manifest.Namespace is null || !QualifiedIdentifier().IsMatch(manifest.Namespace) ||
            manifest.EntryMethod is null || !Identifier().IsMatch(manifest.EntryMethod) ||
            (manifest.DesktopEntryMethod is { } desktopEntry && !Identifier().IsMatch(desktopEntry)))
        {
            throw new ArgumentException("An assembly name, entry namespace and method are required.", nameof(manifest));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.TestMethod);
        replay.Format(manifest.TestMethod);

        if (!Enum.IsDefined(manifest.InputKind) || manifest.SourceFiles.IsDefault)
        {
            throw new ArgumentException("A supported input kind and source list are required.", nameof(manifest));
        }
        if (manifest.InputKind == CorpusInputKind.CSharp)
        {
            sourceNames.Verify(manifest.SourceFiles);
        }
        else if (!manifest.SourceFiles.IsEmpty || manifest.OracleMode != OracleMode.SameIl)
        {
            throw new ArgumentException("Emitted cases require same-IL execution and no source assets.", nameof(manifest));
        }
        matrices.Expand(manifest.InputKind, new(manifest.MatrixProfile, Backend: manifest.ExecutionBackend));

        // Frozen observations require receipt-bound evidence and are not a live
        // authoring mode. Do not silently treat them as same-source execution.
        if (manifest.OracleMode is not (OracleMode.SameIl or OracleMode.SameSource) ||
            (manifest.OracleMode == OracleMode.SameIl && manifest.SameSourceReason is not null) ||
            (manifest.OracleMode == OracleMode.SameSource && string.IsNullOrWhiteSpace(manifest.SameSourceReason)))
        {
            throw new ArgumentException("Live oracle mode and same-source rationale contradict each other.", nameof(manifest));
        }
        const OracleRuntimeCapabilities knownCapabilities = OracleRuntimeCapabilities.GarbageCollection |
            OracleRuntimeCapabilities.Finalization | OracleRuntimeCapabilities.WeakReferenceClearing;
        if ((manifest.RequiredRuntimeCapabilities & ~knownCapabilities) != 0)
        {
            throw new ArgumentException("Unknown runtime capability.", nameof(manifest));
        }
        if (manifest.ReferencePaths.IsDefault ||
            manifest.ReferencePaths.Any(string.IsNullOrWhiteSpace) ||
            manifest.ReferencePaths.Distinct(StringComparer.Ordinal).Count() != manifest.ReferencePaths.Length)
        {
            throw new ArgumentException("Reference paths must be explicitly declared and distinct.", nameof(manifest));
        }
        if (manifest.ReferenceAssemblyAliases is null ||
            manifest.ReferenceAssemblyAliases.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new ArgumentException("Reference aliases must name both assembly identities.", nameof(manifest));
        }
        if (manifest.Inputs.IsDefaultOrEmpty || manifest.Inputs.Distinct().Count() != manifest.Inputs.Length)
        {
            throw new ArgumentException("Dispatcher inputs must be nonempty and distinct.", nameof(manifest));
        }
        if (manifest.Expectations.IsDefault)
        {
            throw new ArgumentException("Declare independent expectations, or an explicit empty list.", nameof(manifest));
        }
        var expectedInputs = new HashSet<int>();
        foreach (var expectation in manifest.Expectations)
        {
            if (expectation is null || !manifest.Inputs.Contains(expectation.Input) ||
                !expectedInputs.Add(expectation.Input))
            {
                throw new ArgumentException("An expectation must identify one distinct declared input.", nameof(manifest));
            }
            if (expectation.ReturnValue.HasValue == (expectation.ExceptionType is not null) ||
                (expectation.ExceptionType is { } type && string.IsNullOrWhiteSpace(type)))
            {
                throw new ArgumentException("Expect exactly one int32 value or managed exception type.", nameof(manifest));
            }
        }
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CaseId();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedIdentifier();
}
