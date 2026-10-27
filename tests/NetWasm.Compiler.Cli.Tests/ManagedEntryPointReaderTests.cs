using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class ManagedEntryPointReaderTests
{
    [Fact]
    public void ReadsTheEntryPointThePortableExecutableSelects()
    {
        // This test assembly is an executable; its generated entry point is Main(string[]).
        var entryPoint = new ManagedEntryPointReader().Read(typeof(ManagedEntryPointReaderTests).Assembly.Location);

        Assert.Equal("Main", entryPoint.MethodName);
        Assert.True(entryPoint.MetadataToken > 0x06000000);
        Assert.Equal(ManagedExecutableParameterShape.StringArray, entryPoint.Abi.ParameterShape);
    }

    [Fact]
    public void PassesTheAssemblyBytesToTheSelector()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-entry-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, [1, 2, 3]);
        var abi = new ManagedExecutableEntryPointAbi(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void);
        var selector = new RecordingSelector(new("Example.Program", "Main", 0x06000002, abi));
        try
        {
            var entryPoint = new ManagedEntryPointReader(selector).Read(path);

            Assert.Equal(new ManagedEntryPoint("Example.Program", "Main", 0x06000002, abi), entryPoint);
            Assert.Equal(path, selector.Path);
            Assert.Equal([1, 2, 3], selector.Image);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsAnEmptyPath(string path) =>
        Assert.Throws<ArgumentException>(() => new ManagedEntryPointReader().Read(path));

    [Fact]
    public void RejectsAMissingSelector() =>
        Assert.Throws<ArgumentNullException>(() => new ManagedEntryPointReader(null!));

    private sealed class RecordingSelector(ManagedExecutableEntryPointSelection entryPoint) :
        IManagedExecutableEntryPointSelector
    {
        public string? Path { get; private set; }
        public byte[]? Image { get; private set; }

        public ManagedExecutableEntryPointSelection SelectEntryPoint(
            string assemblyPath, ReadOnlyMemory<byte> image, int? selectedMethodToken = null)
        {
            Path = assemblyPath;
            Image = image.ToArray();
            return entryPoint;
        }
    }
}
