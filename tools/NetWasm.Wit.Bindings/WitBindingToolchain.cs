using NetWasm.Toolchain.Manifest;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Wit.Bindings;

internal static class WitBindingToolchain
{
    private static readonly HostExecutableResolutionRequest NodeRequest = new(
        HostToolIds.Node,
        "node",
        "NETWASM_NODE_PATH",
        [new("EMSDK_NODE")],
        []);

    public static ExternalToolCommand ResolveWasmToolsCommand(string packageRoot)
    {
        try
        {
            return ResolveWasmToolsCommandCore(packageRoot);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          InvalidDataException or
                                          IOException or
                                          UnauthorizedAccessException)
        {
            throw WitBindingException.Invalid(
                $"unable to resolve the packaged wasm-tools runtime: {exception.Message}");
        }
    }

    private static ExternalToolCommand ResolveWasmToolsCommandCore(string packageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            throw new ArgumentException(
                "The WIT binding tool package root must be absolute.",
                nameof(packageRoot));
        }

        var convention = new HostExecutablePathConvention(
            Path.PathSeparator,
            OperatingSystem.IsWindows() ? ".exe" : string.Empty,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        var node = new HostExecutablePathResolver(
            new SystemHostEnvironmentVariableReader(),
            new FilePresenceChecker(),
            new SystemHostPathCanonicalizer(),
            convention).Resolve(NodeRequest);
        var observation = new ProcessHostToolCompatibilityProbe().Observe(node);
        var compatibility = new HostToolCompatibilityValidator(new(
            HostToolIds.Node,
            new Version(24, 0),
            null,
            [])).Validate(observation);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var manifestPath = Path.Combine(root, "toolchain-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ToolchainAssetMissingException(manifestPath);
        }

        var assets = new PinnedPlatformAssetPathResolver(
            root,
            new JsonToolchainManifestReader().Read(manifestPath),
            new FilePresenceChecker(),
            new Sha256ArtifactDigestVerifier());
        var wasmTools = new WasmToolsToolchainPathResolver(assets)
            .Resolve(node, compatibility);
        return new ExternalToolCommand(
            node.AbsolutePath,
            [wasmTools.CommandPath, wasmTools.ModulePath]);
    }
}
