using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// Schema 1 describes the existing int32 dispatcher contract, not a general
// numeric expression language. Other numeric values belong in source/typed traces.
internal sealed record CorpusCaseManifest(
    int SchemaVersion,
    string CaseId,
    ImmutableArray<string> FeatureIds,
    CorpusInputKind InputKind,
    ImmutableArray<string> SourceFiles,
    string Name,
    string Namespace,
    string EntryMethod,
    ImmutableArray<int> Inputs,
    ImmutableArray<CorpusCaseExpectation> Expectations,
    ImmutableArray<string> ReferencePaths,
    OracleMode OracleMode,
    CorpusMatrixProfile MatrixProfile,
    string TestMethod)
{
    // An independent managed oracle can differ from the target profile's entry.
    // Omitted declarations retain the original shared-entry contract.
    public string? DesktopEntryMethod { get; init; }

    public string? SameSourceReason { get; init; }

    public CorpusExecutionBackend? ExecutionBackend { get; init; }

    public ImmutableDictionary<string, string> ReferenceAssemblyAliases { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    public OracleRuntimeCapabilities RequiredRuntimeCapabilities { get; init; }

    public bool AllowUnsafe { get; init; }

    public bool RequiresReactor { get; init; }

    public bool SupportsBatchedOracle { get; init; }

    public bool ReportAllMismatches { get; init; }

    public bool ExposesLegacyTrace { get; init; } = true;

    public bool UsesTypedTrace { get; init; }
}

internal sealed record CorpusCaseExpectation(int Input, int? ReturnValue, string? ExceptionType);

internal sealed record CorpusFeatureCatalog(ImmutableHashSet<string> Ids);
