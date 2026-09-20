using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Environment;

public sealed class HostingBuildEnvironmentResolver(
    IHostExecutablePathResolver executables,
    IHostToolCompatibilityProbe compatibilityProbe,
    IHostToolCompatibilityValidatorResolver compatibilityValidators,
    IToolchainPackagePathResolver packagePaths,
    IHostToolsPackageResolver? hostPackages,
    IHostToolsExecutableChooser? overrides) : IHostingBuildEnvironmentResolver
{
    public HostingBuildEnvironmentResolver(
        IHostExecutablePathResolver executables,
        IHostToolCompatibilityProbe compatibilityProbe,
        IHostToolCompatibilityValidatorResolver compatibilityValidators,
        IToolchainPackagePathResolver packagePaths)
        : this(executables, compatibilityProbe, compatibilityValidators, packagePaths, null, null)
    {
    }

    private static readonly ImmutableArray<HostExecutableResolutionRequest> ToolRequests =
    [
        new(
            HostToolIds.Node,
            "node",
            "NETWASM_NODE_PATH",
            [new("EMSDK_NODE")],
            []),
        new(
            HostToolIds.WasmLd,
            "wasm-ld",
            "NETWASM_WASM_LD_PATH",
            [],
            [new("EMSDK", Path.Combine("upstream", "bin"))]),
    ];

    private static readonly ImmutableArray<HostExecutableRootFallback> EmsdkRoots =
    [
        new("NETWASM_EMSDK_ROOT", Path.Combine("upstream", "bin")),
        new("EMSDK", Path.Combine("upstream", "bin")),
        new("EMSDK_ROOT", Path.Combine("upstream", "bin")),
    ];

    private static readonly HostExecutableResolutionRequest WasmMergeRequest = new(
        HostToolIds.BinaryenWasmMerge,
        "wasm-merge",
        "NETWASM_WASM_MERGE_PATH",
        [],
        EmsdkRoots,
        SearchPath: false);

    private static readonly HostExecutableResolutionRequest WasmOptRequest = new(
        HostToolIds.BinaryenWasmOpt,
        "wasm-opt",
        "NETWASM_WASM_OPT_PATH",
        [],
        EmsdkRoots,
        SearchPath: false);

    private readonly IHostExecutablePathResolver _executables = executables ??
        throw new ArgumentNullException(nameof(executables));
    private readonly IHostToolCompatibilityProbe _compatibilityProbe = compatibilityProbe ??
        throw new ArgumentNullException(nameof(compatibilityProbe));
    private readonly IHostToolCompatibilityValidatorResolver _compatibilityValidators =
        compatibilityValidators ?? throw new ArgumentNullException(nameof(compatibilityValidators));
    private readonly IToolchainPackagePathResolver _packagePaths = packagePaths ??
        throw new ArgumentNullException(nameof(packagePaths));
    private readonly IHostToolsPackageResolver? _hostPackages = hostPackages;
    private readonly IHostToolsExecutableChooser? _overrides = overrides;

    public HostingBuildEnvironment Resolve(HostingBuildEnvironmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToolchainPackageRoot);
        if (!Path.IsPathFullyQualified(request.ToolchainPackageRoot))
        {
            throw new ArgumentException(
                "The Toolchain package root must be absolute.",
                nameof(request));
        }

        if (request.HostToolsPackageRoot is not null)
        {
            return ResolveRestoredTools(request);
        }

        var products = ToolRequests
            .Select(ResolveTool)
            .ToImmutableDictionary(product => product.Executable.ToolId, StringComparer.Ordinal);
        var node = products[HostToolIds.Node];
        return new(
            node.Executable,
            node.Compatibility,
            products[HostToolIds.WasmLd].Executable,
            products[HostToolIds.WasmLd].Compatibility,
            new(
                ResolveOptionalTool(WasmMergeRequest),
                ResolveOptionalTool(WasmOptRequest)),
            _packagePaths.Resolve(
                Path.GetFullPath(request.ToolchainPackageRoot),
                node.Executable,
                node.Compatibility));
    }

    private HostingBuildEnvironment ResolveRestoredTools(HostingBuildEnvironmentRequest request)
    {
        if (_hostPackages is null || _overrides is null)
        {
            throw new InvalidOperationException("Restored host-tool resolution is not configured.");
        }
        var package = _hostPackages.Resolve(new(
            request.HostToolsPackageRoot!,
            request.HostToolsPackageId ?? string.Empty,
            request.HostToolsPackageVersion ?? string.Empty,
            request.HostRid ?? string.Empty));
        HostToolProduct Tool(string role, string toolId, string exactVersion)
        {
            var packaged = new ResolvedHostExecutable(
                toolId,
                package.Roles[role],
                HostExecutableResolutionSource.Package);
            var selected = _overrides.Choose(packaged);
            return ResolveTool(selected, exactVersion);
        }

        var node = Tool("node", HostToolIds.Node, package.NodeVersion);
        var linker = Tool("wasm-ld", HostToolIds.WasmLd, package.WasmLdVersion);
        var merge = Tool("wasm-merge", HostToolIds.BinaryenWasmMerge, package.BinaryenVersion);
        var opt = Tool("wasm-opt", HostToolIds.BinaryenWasmOpt, package.BinaryenVersion);
        return new(
            node.Executable,
            node.Compatibility,
            linker.Executable,
            linker.Compatibility,
            new(new(merge.Executable, merge.Compatibility),
                new(opt.Executable, opt.Compatibility)),
            _packagePaths.Resolve(
                Path.GetFullPath(request.ToolchainPackageRoot),
                node.Executable,
                node.Compatibility));
    }

    private NativeBinaryenTool? ResolveOptionalTool(
        HostExecutableResolutionRequest request)
    {
        ResolvedHostExecutable executable;
        try
        {
            executable = _executables.Resolve(request) ??
                throw new InvalidOperationException(
                    "The host executable resolver returned no product.");
        }
        catch (HostExecutableResolutionException exception) when (
            exception.Failure is HostExecutableResolutionFailure.PathUnavailable
                or HostExecutableResolutionFailure.ExecutableNotFound)
        {
            return null;
        }

        try
        {
            var observation = _compatibilityProbe.Observe(executable) ??
                throw new InvalidOperationException(
                    "The host compatibility probe returned no observation.");
            var validator = _compatibilityValidators.Resolve(request.ToolId) ??
                throw new InvalidOperationException(
                    "The host compatibility resolver returned no validator.");
            var compatibility = validator.Validate(observation) ??
                throw new InvalidOperationException(
                    "The host compatibility validator returned no product.");
            return new(executable, compatibility);
        }
        catch (Exception exception) when (
            executable.Source == HostExecutableResolutionSource.Path &&
            exception is InvalidOperationException or HostToolCompatibilityException)
        {
            return null;
        }
    }

    private HostToolProduct ResolveTool(HostExecutableResolutionRequest request)
    {
        var executable = _executables.Resolve(request) ??
            throw new InvalidOperationException(
                "The host executable resolver returned no product.");
        return ResolveTool(executable, null);
    }

    private HostToolProduct ResolveTool(ResolvedHostExecutable executable, string? exactPackageVersion)
    {
        var observation = _compatibilityProbe.Observe(executable) ??
            throw new InvalidOperationException(
                "The host compatibility probe returned no observation.");
        var validator = _compatibilityValidators.Resolve(executable.ToolId) ??
            throw new InvalidOperationException(
                "The host compatibility resolver returned no validator.");
        var compatibility = validator.Validate(observation) ??
            throw new InvalidOperationException(
                "The host compatibility validator returned no product.");
        if (executable.Source == HostExecutableResolutionSource.Package)
        {
            var expected = exactPackageVersion!.Contains('.')
                ? exactPackageVersion
                : $"{exactPackageVersion}.0";
            if (!Version.TryParse(expected, out var expectedVersion) ||
                compatibility.Version != expectedVersion)
            {
                throw new InvalidDataException(
                    $"Restored host tool '{executable.ToolId}' is version {compatibility.Version}; expected {exactPackageVersion}.");
            }
        }
        return new(executable, compatibility);
    }

    private sealed record HostToolProduct(
        ResolvedHostExecutable Executable,
        ValidatedHostToolCompatibility Compatibility);
}
