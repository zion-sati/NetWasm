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
        var arguments = new List<string>
        {
            "dotnet",
            "run",
            "--project",
            "src/NetWasm.Compiler.Cli/NetWasm.Compiler.Cli.csproj",
            "--",
            "--input",
            options.EntryAssemblyPath,
            "--output",
            "reproduction.wasm",
            "--entry",
            $"{options.EntryTypeName}::{options.EntryMethodName}",
            "--target",
            options.Target == Core.WasmTarget.Wasm64 ? "wasm64" : "wasm32",
        };
        foreach (var reference in options.ReferencePaths)
        {
            arguments.Add("--reference");
            arguments.Add(reference);
        }
        foreach (var export in options.Exports)
        {
            arguments.Add("--export");
            arguments.Add($"{export.Name}={export.TypeName}::{export.MethodName}");
        }
        foreach (var source in _sourcePaths.Provide(options))
        {
            arguments.Add("--source");
            arguments.Add(source);
        }
        if (options.WitPath is not null)
        {
            arguments.Add("--wit");
            arguments.Add(options.WitPath);
        }
        if (options.WitWorld is not null)
        {
            arguments.Add("--world");
            arguments.Add(options.WitWorld);
        }
        return string.Join(' ', arguments.Select(Quote));
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
