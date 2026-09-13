using NetWasm.Sdk.Pack.Packing;

namespace NetWasm.Sdk.Pack.Archives;

public sealed record PackageArchiveNormalizationRequest(
    string InputPath,
    string OutputPath,
    PackageIdentity Identity,
    DeterminismPolicy Policy);
