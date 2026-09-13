namespace NetWasm.Testing.VSTest.Hosting;

internal sealed record PortableTestHostAssets(
    string TestHostPath,
    string DependencyManifestPath,
    string RuntimeConfigurationPath,
    string PackageProbingPath);
