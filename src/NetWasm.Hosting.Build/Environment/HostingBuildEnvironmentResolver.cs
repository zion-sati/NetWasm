using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Environment;

public sealed class HostingBuildEnvironmentResolver(
    IHostExecutablePathResolver executables,
    IHostToolCompatibilityProbe compatibilityProbe,
    IHostToolCompatibilityValidatorResolver compatibilityValidators,
    IToolchainPackagePathResolver packagePaths) : IHostingBuildEnvironmentResolver
{
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

    private readonly IHostExecutablePathResolver _executables = executables ??
        throw new ArgumentNullException(nameof(executables));
    private readonly IHostToolCompatibilityProbe _compatibilityProbe = compatibilityProbe ??
        throw new ArgumentNullException(nameof(compatibilityProbe));
    private readonly IHostToolCompatibilityValidatorResolver _compatibilityValidators =
        compatibilityValidators ?? throw new ArgumentNullException(nameof(compatibilityValidators));
    private readonly IToolchainPackagePathResolver _packagePaths = packagePaths ??
        throw new ArgumentNullException(nameof(packagePaths));

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

        var products = ToolRequests
            .Select(ResolveTool)
            .ToImmutableDictionary(product => product.Executable.ToolId, StringComparer.Ordinal);
        var node = products[HostToolIds.Node];
        return new(
            node.Executable,
            node.Compatibility,
            products[HostToolIds.WasmLd].Executable,
            products[HostToolIds.WasmLd].Compatibility,
            _packagePaths.Resolve(
                Path.GetFullPath(request.ToolchainPackageRoot),
                node.Executable,
                node.Compatibility));
    }

    private HostToolProduct ResolveTool(HostExecutableResolutionRequest request)
    {
        var executable = _executables.Resolve(request) ??
            throw new InvalidOperationException(
                "The host executable resolver returned no product.");
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

    private sealed record HostToolProduct(
        ResolvedHostExecutable Executable,
        ValidatedHostToolCompatibility Compatibility);
}
