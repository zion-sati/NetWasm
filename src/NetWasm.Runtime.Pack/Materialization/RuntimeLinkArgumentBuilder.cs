using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeLinkArgumentBuilder(IRuntimeLinkExportPlanBuilder exports) : IRuntimeLinkArgumentBuilder
{
    private readonly IRuntimeLinkExportPlanBuilder _exports = exports ?? throw new ArgumentNullException(nameof(exports));
    public ImmutableArray<string> Build(RuntimeLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.Layout);
        var hasCallbacks = request.NativeCallbackSupport is not null;
        if (request.NativeBindings.IsDefault ||
            hasCallbacks != !string.IsNullOrWhiteSpace(request.NativeCallbackObjectPath) ||
            hasCallbacks != !string.IsNullOrWhiteSpace(request.NativeCallbackAllowedUndefinedPath) ||
            request.NativeBindings.IsEmpty && !hasCallbacks &&
            request.Layout.InitialMemorySizeBytes is null)
            throw new InvalidOperationException("A native layout probe requires selected native symbol bindings.");
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add(request.Target.Target == "wasm64" ? "-mwasm64" : "-mwasm32");
        arguments.Add("-Bstatic");
        arguments.Add("--strip-debug");
        arguments.Add("--table-base=1");
        arguments.Add("--whole-archive");
        arguments.Add(ResolveAsset(request.AssetRoot, request.Target.RuntimeArchive.Path));
        arguments.Add("--no-whole-archive");
        arguments.Add(ResolveAsset(request.AssetRoot, request.Target.CollectorArchive.Path));
        if (hasCallbacks)
        {
            arguments.Add(Path.GetFullPath(request.NativeCallbackObjectPath!));
        }
        foreach (var path in request.NativeBindings.Select(binding => binding.Provider.Path).Distinct(StringComparer.Ordinal))
            arguments.Add(Path.GetFullPath(path));
        foreach (var input in request.SystemLibraryPaths)
        {
            arguments.Add(Path.GetFullPath(input));
        }

        var allowedUndefinedPath = hasCallbacks
            ? Path.GetFullPath(request.NativeCallbackAllowedUndefinedPath!)
            : ResolveAsset(
                request.AssetRoot,
                request.Target.AllowedUndefinedSymbols.Path);
        arguments.Add($"--allow-undefined-file={allowedUndefinedPath}");
        arguments.Add("--no-entry");
        arguments.Add("--gc-sections");
        arguments.Add("--no-stack-first");
        arguments.Add($"--global-base={Format(request.Layout.RuntimeGlobalBase)}");
        arguments.Add("-z");
        arguments.Add($"stack-size={Format(request.Target.NativeStackSizeBytes)}");
        if (request.Layout.InitialMemorySizeBytes is { } initialMemory)
            arguments.Add($"--initial-memory={Format(initialMemory)}");
        arguments.Add($"--max-memory={Format(request.Layout.MaximumMemorySizeBytes)}");
        arguments.Add("--export-memory");
        arguments.Add("--export-table");
        var exportPlan = _exports.Build(new(request.Manifest.Exports, request.NativeBindings)
        {
            NativeCallbackSupport = request.NativeCallbackSupport,
        });
        arguments.AddRange(exportPlan.Arguments);
        if (!request.NativeBindings.IsEmpty)
        {
            foreach (var binding in request.NativeBindings)
            {
                arguments.Add($"--undefined={binding.Import.EntryPoint}");
                arguments.Add($"--trace-symbol={binding.Import.EntryPoint}");
            }
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
