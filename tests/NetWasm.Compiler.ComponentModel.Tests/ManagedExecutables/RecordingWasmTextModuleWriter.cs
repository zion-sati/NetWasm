namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

internal sealed class RecordingWasmTextModuleWriter : IWasmTextModuleWriter
{
    public string Source { get; private set; } = string.Empty;

    public string OutputPath { get; private set; } = string.Empty;

    public void Write(string source, string outputPath)
    {
        Source = source;
        OutputPath = outputPath;
    }
}
