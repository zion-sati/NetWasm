namespace NetWasm.Compiler.Cli.Tests;

public sealed class FileCapabilitiesTests
{
    [Fact]
    public void ReadWriteAndDeleteFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwasm-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var binary = Path.Combine(directory, "module.wasm");
            var text = Path.Combine(directory, "manifest.json");
            new SystemTextFileWriter().Write(text, "source");
            new SystemBinaryFileWriter().Write(binary, [1, 2, 3]);

            Assert.Equal("source", new SystemTextFileReader().Read(text));
            Assert.Equal([1, 2, 3], File.ReadAllBytes(binary));
            new SystemFileDeleter().Delete(binary);
            Assert.False(File.Exists(binary));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
