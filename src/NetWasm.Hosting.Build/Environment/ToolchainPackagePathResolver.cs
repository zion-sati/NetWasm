using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Hosting.Build.Environment;

public sealed class ToolchainPackagePathResolver : IToolchainPackagePathResolver
{
    public ToolchainPackagePaths Resolve(
        string packageRoot,
        ResolvedHostExecutable node,
        ValidatedHostToolCompatibility nodeCompatibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            throw new ArgumentException(
                "The Toolchain package root must be absolute.",
                nameof(packageRoot));
        }
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(nodeCompatibility);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var manifestPath = Path.Combine(root, "tools", "toolchain-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ToolchainAssetMissingException(manifestPath);
        }

        var manifest = new JsonToolchainManifestReader().Read(manifestPath);
        var assets = new PinnedPlatformAssetPathResolver(
            root,
            manifest,
            new FilePresenceChecker(),
            new Sha256ArtifactDigestVerifier());
        var wasmTools = new WasmToolsToolchainPathResolver(assets)
            .Resolve(node, nodeCompatibility);
        var raw = new RawInspectionToolchainPathResolver(assets)
            .Resolve(node, nodeCompatibility);
        var jco = new JcoToolchainPathResolver(
            root,
            assets,
            new JcoClosureIntegrityVerifier())
            .Resolve(node, nodeCompatibility);
        var bundle = new HostingBundleToolchainPathResolver(
            root,
            assets,
            new HostingBundleClosureIntegrityVerifier())
            .Resolve(node, nodeCompatibility);
        var witPackages = new WitPackagePathResolver(assets).Resolve();
        RequireOnePackageIdentity(manifest, wasmTools, raw, jco, bundle, witPackages);

        var preview2ShimRoot = Path.Combine(
            root,
            "tools",
            "jco",
            "node_modules",
            "@bytecodealliance",
            "preview2-shim");
        if (!Directory.Exists(preview2ShimRoot))
        {
            throw new InvalidDataException(
                "The Toolchain package is missing the Preview 2 shim closure.");
        }

        return new(
            manifest.PackageId,
            manifest.PackageVersion,
            manifestPath,
            witPackages,
            preview2ShimRoot,
            wasmTools,
            raw,
            jco,
            bundle);
    }

    private static void RequireOnePackageIdentity(
        PinnedToolchainManifest manifest,
        WasmToolsToolchainPaths wasmTools,
        RawInspectionToolchainPaths raw,
        JcoToolchainPaths jco,
        HostingBundleToolchainPaths bundle,
        WitPackagePaths witPackages)
    {
        if (!string.Equals(manifest.PackageId, wasmTools.PackageId, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageId, raw.PackageId, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageId, jco.PackageId, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageId, bundle.PackageId, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageId, witPackages.PackageId, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageVersion, wasmTools.PackageVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageVersion, raw.PackageVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageVersion, jco.PackageVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageVersion, bundle.PackageVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.PackageVersion, witPackages.PackageVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Resolved Toolchain assets do not share the manifest package identity.");
        }
    }
}
