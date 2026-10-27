using NetWasm.Sdk.Pack;
namespace NetWasm.Sdk.Pack.Diagnostics;

public sealed class FilePackDiagnosticWriter : IPackDiagnosticWriter
{
    public void Write(string? outputPath, NetWasmPackErrorCode code, string message)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            Directory.CreateDirectory(directory!);
            var json = System.Text.Json.JsonSerializer.Serialize(
                new PackDiagnosticFile(code.ToString(), message),
                SdkPackJsonContext.Default.PackDiagnosticFile);
            File.WriteAllText(outputPath, json);
        }
        catch (Exception)
        {
            // Diagnostics must never hide the stable pack error, regardless of
            // whether the path, directory, serializer, or file system failed.
        }
    }
}

internal sealed record PackDiagnosticFile(
    [property: System.Text.Json.Serialization.JsonPropertyName("code")] string Code,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message);
