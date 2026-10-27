using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NetWasm.Compiler.Diagnostics;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticArtifactWriterTests
{
    private static readonly JsonTypeInfo<CompilerStatusArtifact> StatusJson =
        CompilerDiagnosticArtifactJsonContext.Default.CompilerStatusArtifact;

    [Fact]
    public void WritesJsonTextBytesAndCopiedFilesBelowTheirRequestedPaths()
    {
        using var assets = TestAssets.Create();
        var directory = Path.Combine(assets.Directory, "artifacts");
        var source = Path.Combine(assets.Directory, "source.cs");
        File.WriteAllText(source, "source");
        var jsonPath = Path.Combine(directory, "state/value.json");
        var textPath = Path.Combine(directory, "state/value.txt");
        var bytesPath = Path.Combine(directory, "binary/value.bin");
        var sourcePath = Path.Combine(directory, "sources/source.cs");

        new CompilerDiagnosticJsonArtifactWriter().WriteJson(
            jsonPath,
            new CompilerStatusArtifact("ready", "test"),
            StatusJson);
        new CompilerDiagnosticTextArtifactWriter().WriteText(textPath, "text");
        new CompilerDiagnosticBinaryArtifactWriter().WriteBinary(bytesPath, [1, 2, 3]);
        new CompilerDiagnosticSourceArtifactCopier().CopySource(
            new FileInfo(source),
            sourcePath);

        var jsonText = File.ReadAllText(jsonPath);
        using var json = JsonDocument.Parse(jsonText);
        Assert.Equal("ready", json.RootElement.GetProperty("status").GetString());
        Assert.Contains('\n', jsonText);
        Assert.DoesNotContain('\r', jsonText);
        Assert.EndsWith("\n", jsonText, StringComparison.Ordinal);
        Assert.Equal("text", File.ReadAllText(textPath));
        byte[] expected = [1, 2, 3];
        Assert.Equal(expected, File.ReadAllBytes(bytesPath));
        Assert.Equal("source", File.ReadAllText(sourcePath));
        Assert.Throws<ArgumentException>(() =>
            new CompilerDiagnosticJsonArtifactWriter().WriteJson("", new CompilerStatusArtifact("ready", "test"), StatusJson));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticJsonArtifactWriter().WriteJson(jsonPath, null!, StatusJson));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticJsonArtifactWriter().WriteJson(jsonPath, new CompilerStatusArtifact("ready", "test"), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticTextArtifactWriter().WriteText(textPath, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticSourceArtifactCopier().CopySource(null!, sourcePath));

        var rootName = "diagnostic-boundary-" + Guid.NewGuid().ToString("N");
        var rootJson = rootName + ".json";
        var rootText = rootName + ".txt";
        var rootBytes = rootName + ".bin";
        var rootSource = rootName + ".cs";
        try
        {
            new CompilerDiagnosticJsonArtifactWriter().WriteJson(rootJson, new CompilerStatusArtifact("root", "test"), StatusJson);
            new CompilerDiagnosticTextArtifactWriter().WriteText(rootText, "root");
            new CompilerDiagnosticBinaryArtifactWriter().WriteBinary(rootBytes, [4]);
            new CompilerDiagnosticSourceArtifactCopier().CopySource(
                new FileInfo(source), rootSource);
            Assert.Equal("root", File.ReadAllText(rootText));
            Assert.Equal("source", File.ReadAllText(rootSource));
            Assert.Equal([4], File.ReadAllBytes(rootBytes));
        }
        finally
        {
            File.Delete(rootJson);
            File.Delete(rootText);
            File.Delete(rootBytes);
            File.Delete(rootSource);
        }
    }
}
