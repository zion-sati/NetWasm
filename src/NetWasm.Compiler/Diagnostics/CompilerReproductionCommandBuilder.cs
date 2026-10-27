using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerReproductionCommandBuilder(
    ICompilerSourcePathProvider sourcePaths) : ICompilerReproductionCommandBuilder
{
    private readonly ICompilerSourcePathProvider _sourcePaths =
        sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));

    public string Build(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // The nwc command line that repeats this compilation from a source checkout.
        var arguments = new List<string>
        {
            "dotnet",
            "run",
            "--project",
            "src/NetWasm.Compiler.Cli/NetWasm.Compiler.Cli.csproj",
            "--",
            options.EntryAssemblyPath,
            "-out:reproduction.wasm",
            "-platform:" + (options.Target == Core.WasmTarget.Wasm64 ? "wasm64" : "wasm32"),
            "-target:" + options.EntryPointKind switch
            {
                CompilerEntryPointKind.ManagedExecutable => "exe",
                CompilerEntryPointKind.Library => "library",
                _ => "function",
            },
        };
        if (options.EntryPointKind == CompilerEntryPointKind.RawFunction)
        {
            arguments.Add($"-entry:{options.EntryTypeName}::{options.EntryMethodName}");
        }
        arguments.AddRange(options.ReferencePaths.Select(reference => "-r:" + reference));
        arguments.AddRange(options.Exports.Select(export =>
            $"-export:{export.Name}={export.TypeName}::{export.MethodName}"));
        arguments.AddRange(_sourcePaths.Provide(options).Select(source => "-source:" + source));
        if (options.WitPath is not null)
        {
            arguments.Add("-wit:" + options.WitPath);
        }
        if (options.WitWorld is not null)
        {
            arguments.Add("-world:" + options.WitWorld);
        }
        if (options.UseJavaScriptExportBoundary)
        {
            arguments.Add("-jsexportboundary+");
        }
        return string.Join(' ', arguments.Select(Quote));
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
