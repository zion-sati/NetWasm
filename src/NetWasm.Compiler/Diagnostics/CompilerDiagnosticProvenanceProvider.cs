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

    public object ProvideProvenance(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new
        {
            Compiler = typeof(NetWasmCompiler).Assembly.GetName().Version!.ToString(),
            DotNet = Environment.Version.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            WasmTools = ReadToolVersion(),
            Assemblies = new[] { options.EntryAssemblyPath }
                .Concat(options.ReferencePaths)
                .Select(path => new
                {
                    Path = Path.GetFullPath(path),
                    Sha256 = _files.Exists(path) ? Hash(path) : null,
                    PdbSha256 = _files.Exists(Path.ChangeExtension(path, ".pdb"))
                        ? Hash(Path.ChangeExtension(path, ".pdb"))
                        : null,
                })
                .ToArray(),
        };
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
