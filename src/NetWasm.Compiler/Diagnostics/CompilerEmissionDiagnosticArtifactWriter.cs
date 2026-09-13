using System;
using System.IO;
using System.Security.Cryptography;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerEmissionDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerDiagnosticBinaryArtifactWriter binary,
    ICompilerDiagnosticTextArtifactWriter text,
    ICompilerWasmTextWriter wasmText,
    ICompilerDiagnosticFileExistenceReader files) : ICompilerEmissionDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerDiagnosticBinaryArtifactWriter _binary =
        binary ?? throw new ArgumentNullException(nameof(binary));
    private readonly ICompilerDiagnosticTextArtifactWriter _text =
        text ?? throw new ArgumentNullException(nameof(text));
    private readonly ICompilerWasmTextWriter _wasmText =
        wasmText ?? throw new ArgumentNullException(nameof(wasmText));
    private readonly ICompilerDiagnosticFileExistenceReader _files =
        files ?? throw new ArgumentNullException(nameof(files));

    public void WriteEmission(CompilerOptions options, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(options);
        var wasmPath = _paths.ResolvePath(options, "06-emission.wasm");
        if (wasmPath is null)
        {
            return;
        }
        _binary.WriteBinary(wasmPath, bytes);
        var watPath = _paths.ResolvePath(options, "06-emission.wat")!;
        try
        {
            _wasmText.WriteText(wasmPath, watPath);
        }
        catch (Exception exception)
        {
            _text.WriteText(
                _paths.ResolvePath(options, "06-emission.wat.error.txt")!,
                exception.ToString());
        }
        _json.WriteJson(_paths.ResolvePath(options, "06-emission.json")!, new
        {
            Length = bytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            WatWritten = _files.Exists(watPath),
        });
    }
}
