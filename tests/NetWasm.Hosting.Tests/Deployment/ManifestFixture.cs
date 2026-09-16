using System.Collections.Immutable;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Deployment;

internal static class ManifestFixture
{
    internal static DeploymentManifest Create() => new(
        1,
        new string('a', 64),
        DeploymentKind.Component,
        "netwasm0.1",
        "wasm32",
        "default",
        "wasi-command@0.2.11",
        new("0.2.0-preview.1", "0.2.0-preview.1", "0.2.0-preview.1", "1.0.0", "0.2.0-preview.1", "0.2.0-preview.1"),
        new string('b', 64),
        [],
        [
            new("My App.wasm", "application", "application/wasm", new string('c', 64), null),
            new("receipts/compiler.json", "compiler-receipt", "application/json", new string('d', 64), 1),
        ],
        ["wasi:cli/environment@0.2.11"],
        [new("wasi:cli/environment@0.2.11", "get-environment", [], ["list<tuple<string,string>>"])],
        [new("netwasm:process/command@1.0.0", "run", ["list<string>"], ["s32"])]);

    internal static DeploymentArtifactSnapshot[] CreateSnapshots(DeploymentManifest manifest) =>
        [.. manifest.Artifacts.Select(artifact => new DeploymentArtifactSnapshot(artifact.RelativePath, artifact.Sha256))];

    internal static ExecutionDescriptor CreateDescriptor(DeploymentManifest manifest, string manifestSha256) => new(
        1,
        manifest.BuildFingerprint,
        Path.GetFullPath("application/My App.netwasm.deployment.json"),
        manifestSha256,
        manifest.Versions.Hosting,
        Path.GetFullPath("packages/toolchain/node"),
        Path.GetFullPath("packages/hosting/launch.mjs"),
        [new("NetWasm.Toolchain", "0.2.0-preview.1", Path.GetFullPath("packages/toolchain"), new string('e', 64))]);

    internal static void AssertFunctionsEqual(
        ImmutableArray<DeploymentFunction> expected,
        ImmutableArray<DeploymentFunction> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Interface, actual[index].Interface);
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal<string>(expected[index].Parameters, actual[index].Parameters);
            Assert.Equal<string>(expected[index].Results, actual[index].Results);
        }
    }
}

internal sealed class ManifestValidationStub(Action<DeploymentManifest> validate) : IDeploymentManifestValidator
{
    public void Validate(DeploymentManifest manifest) => validate(manifest);
}

internal sealed class ManifestReaderStub(Func<ReadOnlyMemory<byte>, DeploymentManifest> read) : IDeploymentManifestReader
{
    public DeploymentManifest Read(ReadOnlyMemory<byte> utf8Json) => read(utf8Json);
}

internal sealed class ContentHasherStub(Func<ReadOnlyMemory<byte>, string> hash) : IContentHasher
{
    public string Hash(ReadOnlyMemory<byte> content) => hash(content);
}

internal sealed class ExecutionDescriptorValidationStub(Action<ExecutionDescriptor> validate) : IExecutionDescriptorValidator
{
    public void Validate(ExecutionDescriptor descriptor) => validate(descriptor);
}
