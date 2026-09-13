using System.Collections.Immutable;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed record PortableTestHostManifest(
    int SchemaVersion,
    string TestPlatformVersion,
    ImmutableArray<PortableTestHostManifestFile> Files);

internal sealed record PortableTestHostManifestFile(
    string RelativePath,
    string Sha256);
