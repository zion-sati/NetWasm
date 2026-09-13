using System.Collections.Immutable;
using NetWasm.Hosting.Build.Composition;
using NetWasm.Hosting.Build.Environment;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Hosting.Build.Tests;

#pragma warning disable CA1859 // Contract tests deliberately dispatch through public interfaces.

public sealed class HostingBuildEnvironmentResolverTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-hosting-build"));

    [Fact]
    public void CompositionRequiresNode24AndLld24()
    {
        var requirements = HostingBuildComposition.Requirements();

        Assert.Collection(
            requirements,
            node =>
            {
                Assert.Equal(HostToolIds.Node, node.ToolId);
                Assert.Equal(new Version(24, 0), node.MinimumVersionInclusive);
                Assert.Null(node.MaximumVersionExclusive);
                Assert.Empty(node.RequiredCapabilities);
            },
            lld =>
            {
                Assert.Equal(HostToolIds.WasmLd, lld.ToolId);
                Assert.Equal(new Version(24, 0), lld.MinimumVersionInclusive);
                Assert.Null(lld.MaximumVersionExclusive);
                Assert.Empty(lld.RequiredCapabilities);
            });
    }

    [Fact]
    public void ResolveValidatesEveryPrerequisiteBeforeResolvingPackageAssets()
    {
        var executables = new RecordingExecutableResolver();
        var probe = new RecordingProbe();
        var validators = new RecordingValidatorResolver();
        var packagePaths = new RecordingPackagePathResolver();
        IHostingBuildEnvironmentResolver resolver = new HostingBuildEnvironmentResolver(
            executables,
            probe,
            validators,
            packagePaths);

        var result = resolver.Resolve(new(Root));

        Assert.Equal(HostToolIds.Required.ToArray(), executables.Requests.Select(x => x.ToolId));
        Assert.Collection(
            executables.Requests,
            node =>
            {
                Assert.Equal("NETWASM_NODE_PATH", node.OverrideEnvironmentVariableName);
                Assert.Equal(
                    "EMSDK_NODE",
                    Assert.Single(node.EnvironmentFallbacks).EnvironmentVariableName);
                Assert.Empty(node.RootFallbacks);
            },
            lld =>
            {
                Assert.Empty(lld.EnvironmentFallbacks);
                var fallback = Assert.Single(lld.RootFallbacks);
                Assert.Equal("EMSDK", fallback.RootEnvironmentVariableName);
                Assert.Equal(Path.Combine("upstream", "bin"), fallback.RelativeDirectory);
            });
        Assert.Equal(HostToolIds.Required.ToArray(), probe.Requests.Select(x => x.ToolId));
        Assert.Equal(HostToolIds.Required.ToArray(), validators.Requests);
        Assert.Equal(Root, packagePaths.PackageRoot);
        Assert.Equal(HostToolIds.Node, packagePaths.Node?.ToolId);
        Assert.Equal(new Version(50, 1), packagePaths.NodeCompatibility?.Version);
        Assert.Equal("0.1.0-preview.24", result.Toolchain.PackageVersion);
        Assert.Equal(Path.Combine(Root, "node"), result.Node.AbsolutePath);
        Assert.Equal(Path.Combine(Root, "wasm-ld"), result.WasmLd.AbsolutePath);
        Assert.Equal("1.256.0", result.Toolchain.WasmTools.WasmToolsVersion);
        Assert.Equal(Path.Combine(Root, "node"), result.Toolchain.WasmTools.NodePath);
    }

    [Fact]
    public void ResolveRejectsInvalidRequestsBeforeCapabilitiesRun()
    {
        var executables = new RecordingExecutableResolver();
        var resolver = new HostingBuildEnvironmentResolver(
            executables,
            new RecordingProbe(),
            new RecordingValidatorResolver(),
            new RecordingPackagePathResolver());

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(new("")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(new("relative")));
        Assert.Empty(executables.Requests);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        var executables = new RecordingExecutableResolver();
        var probe = new RecordingProbe();
        var validators = new RecordingValidatorResolver();
        var paths = new RecordingPackagePathResolver();

        Assert.Throws<ArgumentNullException>(() => new HostingBuildEnvironmentResolver(
            null!, probe, validators, paths));
        Assert.Throws<ArgumentNullException>(() => new HostingBuildEnvironmentResolver(
            executables, null!, validators, paths));
        Assert.Throws<ArgumentNullException>(() => new HostingBuildEnvironmentResolver(
            executables, probe, null!, paths));
        Assert.Throws<ArgumentNullException>(() => new HostingBuildEnvironmentResolver(
            executables, probe, validators, null!));
    }

    private sealed class RecordingExecutableResolver : IHostExecutablePathResolver
    {
        private readonly List<HostExecutableResolutionRequest> _requests = [];
        public ImmutableArray<HostExecutableResolutionRequest> Requests => [.. _requests];

        public ResolvedHostExecutable Resolve(HostExecutableResolutionRequest request)
        {
            _requests.Add(request);
            return new(
                request.ToolId,
                Path.Combine(Root, request.ExecutableName),
                HostExecutableResolutionSource.Path);
        }
    }

    private sealed class RecordingProbe : IHostToolCompatibilityProbe
    {
        private readonly List<ResolvedHostExecutable> _requests = [];
        public ImmutableArray<ResolvedHostExecutable> Requests => [.. _requests];

        public HostToolCompatibilityObservation Observe(ResolvedHostExecutable executable)
        {
            _requests.Add(executable);
            return new(executable.ToolId, new Version(50, 1), []);
        }
    }

    private sealed class RecordingValidatorResolver : IHostToolCompatibilityValidatorResolver
    {
        private readonly List<string> _requests = [];
        public ImmutableArray<string> Requests => [.. _requests];

        public IHostToolCompatibilityValidator Resolve(string toolId)
        {
            _requests.Add(toolId);
            return new PassValidator(toolId);
        }

        private sealed class PassValidator(string toolId) : IHostToolCompatibilityValidator
        {
            public ValidatedHostToolCompatibility Validate(
                HostToolCompatibilityObservation observation) =>
                new(toolId, observation.Version, observation.Capabilities);
        }
    }

    private sealed class RecordingPackagePathResolver : IToolchainPackagePathResolver
    {
        public string? PackageRoot { get; private set; }
        public ResolvedHostExecutable? Node { get; private set; }
        public ValidatedHostToolCompatibility? NodeCompatibility { get; private set; }

        public ToolchainPackagePaths Resolve(
            string packageRoot,
            ResolvedHostExecutable node,
            ValidatedHostToolCompatibility nodeCompatibility)
        {
            PackageRoot = packageRoot;
            Node = node;
            NodeCompatibility = nodeCompatibility;
            return new(
                "NetWasm.Toolchain",
                "0.1.0-preview.24",
                Path.Combine(packageRoot, "tools", "toolchain-manifest.json"),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.24",
                    Path.Combine(packageRoot, "tools", "wit-packages", "command.wit.wasm"),
                    Path.Combine(packageRoot, "tools", "wit-packages", "async-command.wit.wasm"),
                    Path.Combine(packageRoot, "tools", "wit-packages", "compiler.wit.wasm")),
                Path.Combine(packageRoot, "tools", "jco", "node_modules", "@bytecodealliance", "preview2-shim"),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.24",
                    "1.256.0",
                    node.AbsolutePath,
                    nodeCompatibility.Version,
                    Path.Combine(packageRoot, "tools", "wasm-tools", "run-wasm-tools.mjs"),
                    Path.Combine(packageRoot, "tools", "wasm-tools", "wasm-tools.wasm"),
                    Path.Combine(packageRoot, "tools", "wasm-tools", "LICENSE-APACHE"),
                    Path.Combine(packageRoot, "tools", "wasm-tools", "LICENSE-Apache-2.0_WITH_LLVM-exception"),
                    Path.Combine(packageRoot, "tools", "wasm-tools", "LICENSE-MIT"),
                    Path.Combine(packageRoot, "tools", "wasm-tools", "README.md")),
                new("NetWasm.Toolchain", "0.1.0-preview.24", node.AbsolutePath,
                    nodeCompatibility.Version, "inspect", "binaryen", "wasm-opt", "wasm-merge"),
                new("NetWasm.Toolchain", "0.1.0-preview.24", "1.28.1", "0.24.1",
                    node.AbsolutePath, nodeCompatibility.Version, "jco", "lock", "integrity", "notices", "policy"),
                new("NetWasm.Toolchain", "0.1.0-preview.24", "1.2.4",
                    node.AbsolutePath, nodeCompatibility.Version, "bundle", "rolldown", "lock", "integrity", "notices", "policy"));
        }
    }
}

#pragma warning restore CA1859
