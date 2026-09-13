using System;
using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerWasmTextWriter(
    IExternalToolRunner tools,
    ICompilerDiagnosticTextArtifactWriter textWriter) : ICompilerWasmTextWriter
{
    private readonly IExternalToolRunner _tools =
        tools ?? throw new ArgumentNullException(nameof(tools));
    private readonly ICompilerDiagnosticTextArtifactWriter _textWriter =
        textWriter ?? throw new ArgumentNullException(nameof(textWriter));

    public void WriteText(string wasmPath, string watPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wasmPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(watPath);
        var result = _tools.Run("wasm-tools", ["print", wasmPath]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"wasm-tools print failed with exit code {result.ExitCode}: " +
                result.StandardError);
        }
        _textWriter.WriteText(watPath, result.StandardOutput);
    }
}
