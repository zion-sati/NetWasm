using System.Collections.Immutable;
using NetWasm.Hosting.Build.Environment;
using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Hosting.Build.Composition;

public static class HostingBuildComposition
{
    public static IDeploymentBuildWriter CreateDeploymentBuildWriter()
    {
        var store = new BuildArtifactStore();
        var hashes = new Sha256ContentHasher();
        return new DeploymentBuildWriter(
            store,
            hashes,
            new DeploymentManifestWriter(new DeploymentManifestValidator()));
    }

    public static ILocalExecutionDescriptorBuildWriter CreateLocalExecutionDescriptorBuildWriter()
    {
        var store = new BuildArtifactStore();
        return new LocalExecutionDescriptorBuildWriter(
            store,
            new Sha256ContentHasher(),
            new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));
    }

    public static IExecutionRequestBuildWriter CreateExecutionRequestBuildWriter() =>
        new ExecutionRequestBuildWriter(
        new BuildArtifactStore(),
        new NetWasmExecutionRequestWriter(new NetWasmExecutionRequestValidator()));

    public static IHostingBuildEnvironmentResolver CreateEnvironmentResolver()
    {
        var convention = new HostExecutablePathConvention(
            Path.PathSeparator,
            OperatingSystem.IsWindows() ? ".exe" : string.Empty,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        var executableResolver = new HostExecutablePathResolver(
            new SystemHostEnvironmentVariableReader(),
            new FilePresenceChecker(),
            new SystemHostPathCanonicalizer(),
            convention);
        var registrations = Requirements()
            .Select(requirement => new HostToolCompatibilityValidatorRegistration(
                requirement.ToolId,
                new HostToolCompatibilityValidator(requirement)))
            .ToImmutableArray();
        return new HostingBuildEnvironmentResolver(
            executableResolver,
            new ProcessHostToolCompatibilityProbe(),
            new HostToolCompatibilityValidatorResolver(HostToolIds.Required, registrations),
            new ToolchainPackagePathResolver());
    }

    internal static ImmutableArray<HostToolCompatibilityRequirement> Requirements() =>
    [
        new(HostToolIds.Node, new Version(24, 0), null, []),
        new(HostToolIds.WasmLd, new Version(24, 0), null, []),
    ];
}
