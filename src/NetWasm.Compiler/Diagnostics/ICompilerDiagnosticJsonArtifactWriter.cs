using System.Text.Json.Serialization.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticJsonArtifactWriter
{
    void WriteJson<T>(string path, T value, JsonTypeInfo<T> typeInfo);
}
