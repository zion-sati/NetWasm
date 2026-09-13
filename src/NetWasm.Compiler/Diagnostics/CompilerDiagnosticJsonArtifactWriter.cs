using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticJsonArtifactWriter :
    ICompilerDiagnosticJsonArtifactWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public void WriteJson(string path, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);
        CreateParentDirectory(path);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, SerializerOptions) + "\n",
            new UTF8Encoding(false));
    }

    private static void CreateParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
