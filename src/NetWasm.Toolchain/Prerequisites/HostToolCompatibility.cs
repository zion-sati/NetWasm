using System;
using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public sealed record HostToolCompatibilityRequirement(
    string ToolId,
    Version MinimumVersionInclusive,
    Version? MaximumVersionExclusive,
    ImmutableArray<string> RequiredCapabilities);

public sealed record HostToolCompatibilityObservation(
    string ToolId,
    Version Version,
    ImmutableArray<string> Capabilities);

public sealed record ValidatedHostToolCompatibility(
    string ToolId,
    Version Version,
    ImmutableArray<string> Capabilities);
