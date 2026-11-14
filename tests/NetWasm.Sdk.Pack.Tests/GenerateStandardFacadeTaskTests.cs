using Microsoft.Build.Utilities;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class GenerateStandardFacadeTaskTests
{
    [Fact]
    public void ReadsInputsAndPreservesUnchangedOutputTimestamp()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var contract = Path.Combine(directory.Path, "contract.dll");
        var owner = Path.Combine(directory.Path, "owner.dll");
        var output = Path.Combine(directory.Path, "generated", "netstandard.dll");
        File.WriteAllBytes(contract, [1]);
        File.WriteAllBytes(owner, [2]);
        var builder = new RecordingBuilder();
        var task = new GenerateStandardFacadeTask(builder)
        {
            ContractPath = contract,
            Owners = [new TaskItem(owner)],
            OutputPath = output,
            BuildEngine = new PackTestFixtures.RecordingBuildEngine()
        };
        Assert.True(task.Execute());
        Assert.Equal(new byte[] { 1 }, builder.Contract);
        Assert.Equal(new byte[] { 2 }, Assert.Single(builder.Owners!));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(output));
        File.SetLastWriteTimeUtc(output, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var timestamp = File.GetLastWriteTimeUtc(output);
        Assert.True(task.Execute());
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(output));
        builder.Output = [4];
        Assert.True(task.Execute());
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(output));
    }

    [Fact]
    public void MissingInputsFailWithoutCreatingAnOutput()
    {
        using var directory = new PackTestFixtures.TemporaryDirectory();
        var output = Path.Combine(directory.Path, "netstandard.dll");
        var task = new GenerateStandardFacadeTask
        {
            ContractPath = Path.Combine(directory.Path, "missing.dll"),
            OutputPath = output,
            BuildEngine = new PackTestFixtures.RecordingBuildEngine()
        };
        Assert.False(task.Execute());
        Assert.False(File.Exists(output));
        Assert.Throws<ArgumentNullException>(() => new GenerateStandardFacadeTask(null!));
    }

    private sealed class RecordingBuilder : IStandardFacadeBuilder
    {
        public byte[]? Contract { get; private set; }
        public IReadOnlyList<byte[]>? Owners { get; private set; }
        public byte[] Output { get; set; } = [3];
        public byte[] Build(byte[] contract, IReadOnlyList<byte[]> owners)
        {
            Contract = contract;
            Owners = owners;
            return Output;
        }

        public FacadeImage BuildFacade(byte[] contract, IReadOnlyList<byte[]> owners) =>
            new("contract", Build(contract, owners), 1, 1);
    }
}
