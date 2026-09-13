using System;
using System.IO;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmTextModuleWriter
{
    void Write(string source, string outputPath);
}

public sealed class WasmTextModuleWriter(
    IWasmTools tools,
    ITextFileWriter files,
    IFileDeleter deletions) : IWasmTextModuleWriter
{
    private readonly IWasmTools _tools = tools ??
        throw new ArgumentNullException(nameof(tools));
    private readonly ITextFileWriter _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IFileDeleter _deletions = deletions ??
        throw new ArgumentNullException(nameof(deletions));

    public void Write(string source, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var sourcePath = outputPath + ".wat";
        try
        {
            _files.Write(sourcePath, source);
            var result = _tools.Run("parse", sourcePath, "--output", outputPath);
            if (result.ExitCode == 0)
            {
                return;
            }
            var error = result.StandardError.Replace('\r', ' ')
                .Replace('\n', ' ').Trim();
            throw ComponentException.Tool(
                $"wasm-tools failed to create link support module " +
                $"'{Path.GetFileName(outputPath)}': " +
                (error.Length == 0 ? "unknown error" : error));
        }
        finally
        {
            _deletions.Delete(sourcePath);
        }
    }
}
