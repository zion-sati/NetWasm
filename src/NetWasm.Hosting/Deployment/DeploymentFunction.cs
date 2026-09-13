using System.Collections.Immutable;

namespace NetWasm.Hosting.Deployment;

/// <summary>An exact versioned interface function imported or exported by the final artifact.</summary>
public sealed record DeploymentFunction(
    string Interface,
    string Name,
    ImmutableArray<string> Parameters,
    ImmutableArray<string> Results);
