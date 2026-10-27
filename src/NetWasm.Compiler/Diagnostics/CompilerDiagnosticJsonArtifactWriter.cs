using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticJsonArtifactWriter :
    ICompilerDiagnosticJsonArtifactWriter
{
    public void WriteJson<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        CreateParentDirectory(path);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, typeInfo) + "\n",
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
