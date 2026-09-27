using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeLinkArgumentBuilder : IRuntimeLinkArgumentBuilder
{
    public ImmutableArray<string> Build(RuntimeLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add(request.Target.Target == "wasm64" ? "-mwasm64" : "-mwasm32");
        arguments.Add("-Bstatic");
        arguments.Add("--strip-debug");
        arguments.Add("--table-base=1");
        arguments.Add("--whole-archive");
        arguments.Add(ResolveAsset(request.AssetRoot, request.Target.RuntimeArchive.Path));
        arguments.Add("--no-whole-archive");
        arguments.Add(ResolveAsset(request.AssetRoot, request.Target.CollectorArchive.Path));
        foreach (var input in request.SystemLibraryPaths)
        {
            arguments.Add(Path.GetFullPath(input));
        }

        arguments.Add($"--allow-undefined-file={ResolveAsset(
            request.AssetRoot, request.Target.AllowedUndefinedSymbols.Path)}");
        arguments.Add("--no-entry");
        arguments.Add("--gc-sections");
        arguments.Add("--no-stack-first");
        arguments.Add($"--global-base={Format(request.Layout.RuntimeGlobalBase)}");
        arguments.Add("-z");
        arguments.Add($"stack-size={Format(request.Target.NativeStackSizeBytes)}");
        arguments.Add($"--initial-memory={Format(request.Layout.InitialMemorySizeBytes)}");
        arguments.Add($"--max-memory={Format(request.Layout.MaximumMemorySizeBytes)}");
        arguments.Add("--export-memory");
        arguments.Add("--export-table");
        arguments.Add("--export=emscripten_stack_get_current");
        arguments.Add("--export=_emscripten_stack_restore");
        arguments.Add("--export-if-defined=__start_em_asm");
        arguments.Add("--export-if-defined=__stop_em_asm");
        arguments.Add("--export-if-defined=__start_em_lib_deps");
        arguments.Add("--export-if-defined=__stop_em_lib_deps");
        arguments.Add("--export-if-defined=__start_em_js");
        arguments.Add("--export-if-defined=__stop_em_js");
        foreach (var export in request.Manifest.Exports)
        {
            arguments.Add($"--export={export}");
        }

        arguments.Add("-mllvm");
        arguments.Add("-combiner-global-alias-analysis=false");
        arguments.Add("-mllvm");
        arguments.Add("-enable-emscripten-sjlj");
        arguments.Add("-mllvm");
        arguments.Add("-disable-lsr");

        arguments.Add("-o");
        arguments.Add(Path.GetFullPath(request.OutputPath));
        return arguments.ToImmutable();
    }

    private static string ResolveAsset(string assetRoot, string relativePath)
    {
        var root = Path.GetFullPath(assetRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The NetWasm runtime pack contains an unsafe asset path.");
        }

        return fullPath;
    }

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
