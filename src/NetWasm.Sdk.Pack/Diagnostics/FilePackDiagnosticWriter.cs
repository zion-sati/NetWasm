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
            var json = System.Text.Json.JsonSerializer.Serialize(new { code = code.ToString(), message });
            File.WriteAllText(outputPath, json);
        }
        catch (Exception)
        {
            // Diagnostics must never hide the stable pack error, regardless of
            // whether the path, directory, serializer, or file system failed.
        }
    }
}
