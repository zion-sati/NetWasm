using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class ComponentManifestWriterTests
{
    [Fact]
    public void WritesCamelCaseManifestAndCreatesItsDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"netwasm-component-manifest-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nested", "component.json");
        try
        {
            var writer = AsWriter(new ComponentManifestWriter());
            writer.Write(new(path, CreateManifest()));

            var json = File.ReadAllText(path);
            Assert.Contains("\"wasiVersion\": \"0.2\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"WasiVersion\"", json, StringComparison.Ordinal);
            Assert.Contains('\n', json);
            Assert.DoesNotContain('\r', json);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RejectsNullRequest()
    {
        var writer = AsWriter(new ComponentManifestWriter());
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    private static ComponentManifest CreateManifest() => new(
        1,
        "wasi:cli@0.2.11",
        "command",
        "wasm32",
        "0.2",
        "utf8",
        "digest",
        "wasm-tools",
        [],
        [],
        [],
        []);

    private static IComponentManifestWriter AsWriter(object writer) =>
        (IComponentManifestWriter)writer;
}
