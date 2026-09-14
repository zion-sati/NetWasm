using NetWasm.Toolchain.Manifest;

namespace NetWasm.Toolchain.Tests;

public sealed class ManifestReaderTests
{
    [Fact]
    public void ReadsPinnedToolManifest()
    {
        var path = WriteTemporaryFile("{\"schemaVersion\":\"1\",\"packageId\":\"NetWasm.Toolchain\",\"packageVersion\":\"0.1.0-test\",\"assets\":[{\"id\":\"binaryen-module\",\"version\":\"132.0.0\",\"relativePath\":\"tools/binaryen/index.js\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}");
        try
        {
            IToolchainManifestReader reader = CreateToolManifestReader();
            var manifest = reader.Read(path);

            Assert.Equal("1", manifest.SchemaVersion);
            Assert.Equal("NetWasm.Toolchain", manifest.PackageId);
            Assert.Equal("0.1.0-test", manifest.PackageVersion);
            Assert.Single(manifest.Assets);
            Assert.Equal("binaryen-module", manifest.Assets[0].Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmptyToolManifestFailsClearly()
    {
        var path = WriteTemporaryFile(string.Empty);
        try
        {
            Assert.Throws<InvalidDataException>(() => new JsonToolchainManifestReader().Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ToolManifestRejectsUnknownProperties()
    {
        var path = WriteTemporaryFile("{\"schemaVersion\":\"1\",\"packageId\":\"NetWasm.Toolchain\",\"packageVersion\":\"0.1.0-test\",\"assets\":[],\"unexpected\":true}");
        try
        {
            Assert.Throws<InvalidDataException>(() => new JsonToolchainManifestReader().Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NullManifestsFailClearly()
    {
        var toolPath = WriteTemporaryFile("null");
        try
        {
            Assert.Throws<InvalidDataException>(() => new JsonToolchainManifestReader().Read(toolPath));
        }
        finally
        {
            File.Delete(toolPath);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ManifestReadersRejectBlankPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => new JsonToolchainManifestReader().Read(path));
    }

    private static string WriteTemporaryFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-toolchain-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static IToolchainManifestReader CreateToolManifestReader()
    {
        IToolchainManifestReader reader = new JsonToolchainManifestReader();
        return reader;
    }
}
