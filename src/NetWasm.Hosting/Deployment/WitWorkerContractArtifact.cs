using System;
using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Hosting.Deployment;

public sealed record WitWorkerContractArtifact
{
    public int SchemaVersion { get; init; }
    public string? World { get; init; }
    public ImmutableArray<WitWorkerExportArtifact?> Exports { get; init; }
    public ImmutableArray<WitWorkerTypeArtifact?> Types { get; init; }
    public WitWorkerReactorArtifact? Reactor { get; init; }
}

public sealed record WitWorkerReactorArtifact
{
    public string? Interface { get; init; }
    public string? JavaScriptRoot { get; init; }
    public string? JavaScriptMember { get; init; }
}

public sealed record WitWorkerExportArtifact
{
    public string? Operation { get; init; }
    public string? Placement { get; init; }
    public string? WorldItem { get; init; }
    public string? Interface { get; init; }
    public string JavaScriptRoot { get; init; } = string.Empty;
    public string? JavaScriptMember { get; init; }
    public string? Function { get; init; }
    public ImmutableArray<WitWorkerParameterArtifact?> Parameters { get; init; }
    public WitWorkerValueReference? Result { get; init; }
    public WitWorkerFunctionKindArtifact? Kind { get; init; }
}

public sealed record WitWorkerParameterArtifact
{
    public string? Name { get; init; }
    public WitWorkerValueReference? Type { get; init; }
}

public sealed record WitWorkerValueReference(string Kind, string? Primitive, int? Definition);

public sealed record WitWorkerFunctionKindArtifact
{
    public string? Name { get; init; }
    public int? ResourceType { get; init; }
}

public sealed record WitWorkerTypeArtifact(
    int Id, string? Name, JsonElement Kind, WitWorkerTypeOwnerArtifact? Owner);

public sealed record WitWorkerTypeOwnerArtifact(string Kind, string Identity);

/// <summary>A validated persisted contract, with artifact-local IDs rather than array offsets.</summary>
public sealed record WitWorkerResolvedContract(
    WitWorkerContractArtifact Artifact,
    ImmutableDictionary<int, WitWorkerTypeArtifact> Definitions,
    ImmutableDictionary<int, ImmutableArray<WitWorkerValueReference>> References)
{
    public ImmutableArray<WitWorkerExportArtifact?> Exports => Artifact.Exports;
    public WitWorkerReactorArtifact? Reactor => Artifact.Reactor;
}

public interface IWitWorkerContractReader
{
    WitWorkerResolvedContract Read(ReadOnlyMemory<byte> bytes);
}
