using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeNativeValidationProfile(
    int Version,
    ImmutableArray<string> Features,
    ImmutableArray<RuntimeNativeImportContract> Imports);

internal sealed record RuntimeNativeImportContract(
    string Module,
    string Name,
    ImmutableArray<byte> Parameters,
    ImmutableArray<byte> Results,
    bool Required);
