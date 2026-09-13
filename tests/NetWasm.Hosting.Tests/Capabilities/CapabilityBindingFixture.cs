using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Tests.Deployment;
using NetWasm.Hosting.Tests.Execution;

namespace NetWasm.Hosting.Tests.Capabilities;

internal sealed record CapabilityBindingFixtureData(
    DeploymentManifest Manifest,
    NetWasmExecutionRequest Request,
    ImmutableArray<NetWasmPlatformImportProvider> PlatformProviders,
    ImmutableArray<NetWasmApplicationImportProvider> ApplicationProviders);

internal static class CapabilityBindingFixture
{
    internal static CapabilityBindingFixtureData Create()
    {
        var platformFunction = new DeploymentFunction(
            "wasi:cli/environment@0.2.11",
            "get-environment",
            [],
            ["list<tuple<string,string>>"]);
        var applicationFunction = new DeploymentFunction(
            "example:logging/logger@1.0.0",
            "log",
            ["string"],
            []);
        var applicationImportArtifact = new DeploymentArtifact(
            "imports/logger.mjs",
            "application-import",
            "text/javascript",
            new string('e', 64),
            null);
        var manifest = ManifestFixture.Create() with
        {
            Artifacts = [.. ManifestFixture.Create().Artifacts, applicationImportArtifact],
            RequiredImportModules = [platformFunction.Interface, applicationFunction.Interface],
            RequiredImports = [platformFunction, applicationFunction],
        };
        var request = ExecutionRequestFixture.Create() with
        {
            BuildFingerprint = manifest.BuildFingerprint,
            ApplicationImports = [new(applicationFunction.Interface, applicationImportArtifact.RelativePath, applicationImportArtifact.Sha256)],
        };
        return new(
            manifest,
            request,
            [new(platformFunction.Interface, NetWasmPlatformCapability.Environment, [platformFunction])],
            [new(applicationFunction.Interface, [applicationFunction])]);
    }

    internal static CapabilityBindingFixtureData WithPlatformCapability(
        NetWasmPlatformCapability capability,
        NetWasmCapabilityGrants grants)
    {
        var data = Create();
        return data with
        {
            Request = data.Request with { Grants = grants },
            PlatformProviders = [data.PlatformProviders[0] with { Capability = capability }],
        };
    }
}
