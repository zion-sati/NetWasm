using System.Text;
using NetWasm.Hosting.Generator;

namespace NetWasm.Hosting.Generator.Tests;

public sealed class TextFileStoreTests
{
    [Fact]
    public void WritesBomlessUtf8AndCreatesTheOutputDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"netwasm-hosting-generator-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "nested", "catalog.mjs");
        try
        {
            var store = new TextFileStore();
            store.Write(path, "héllo");

            Assert.Equal("héllo", store.Read(path));
            Assert.Equal(Encoding.UTF8.GetBytes("héllo"), File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsInvalidReadAndWriteInputs()
    {
        var store = new TextFileStore();

        Assert.Throws<ArgumentException>(() => store.Read(""));
        Assert.Throws<ArgumentException>(() => store.Write(" ", "value"));
        Assert.Throws<ArgumentNullException>(() => store.Write("output", null!));
    }
}
