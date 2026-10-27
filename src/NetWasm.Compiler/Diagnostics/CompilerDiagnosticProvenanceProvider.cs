using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticProvenanceProvider :
    ICompilerDiagnosticProvenanceProvider
{
    private readonly IExternalToolRunner _tools;
    private readonly ICompilerDiagnosticFileExistenceReader _files;
    private readonly ICompilerDiagnosticFileByteReader _bytes;

    public CompilerDiagnosticProvenanceProvider(
        IExternalToolRunner tools,
        ICompilerDiagnosticFileExistenceReader files,
        ICompilerDiagnosticFileByteReader bytes)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public CompilerProvenanceArtifact ProvideProvenance(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new CompilerProvenanceArtifact(
            typeof(NetWasmCompiler).Assembly.GetName().Version!.ToString(),
            Environment.Version.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            ReadToolVersion(),
            [.. new[] { options.EntryAssemblyPath }
                .Concat(options.ReferencePaths)
                .Select(path => new CompilerAssemblyProvenanceArtifact(
                    Path.GetFullPath(path),
                    _files.Exists(path) ? Hash(path) : null,
                    _files.Exists(Path.ChangeExtension(path, ".pdb"))
                        ? Hash(Path.ChangeExtension(path, ".pdb"))
                        : null))]);
    }

    private string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(_bytes.ReadBytes(path)));

    private string? ReadToolVersion()
    {
        try
        {
            var result = _tools.Run("wasm-tools", ["--version"]);
            return result.ExitCode == 0
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();
        }
        catch
        {
            return null;
        }
    }
}
