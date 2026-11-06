using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Pipeline;

/// <summary>
/// What a host writes beside a compile's module, kept with the compile so one the early
/// cutoff serves from it has the same: the options it ran with, its static data end,
/// interop manifest, function imports, runtime features, entry point, and the exception
/// types its diagnostics name.
/// </summary>
internal sealed record KeptCompilationFacts(
    string Options,
    int StaticDataEnd,
    HostInteropManifest InteropManifest,
    ImmutableArray<WasmFunctionImport> FunctionImports,
    ImmutableArray<string> RuntimeFeatures,
    int? EntryPointToken,
    bool SynchronousEntry,
    ImmutableArray<ReachableExceptionType>? ExceptionTypes)
{
    public static byte[] Write(KeptCompilationFacts facts) =>
        JsonSerializer.SerializeToUtf8Bytes(facts, KeptCompilationFactsJsonContext.Default.KeptCompilationFacts);

    // The facts kept, or none for bytes of another shape.
    public static KeptCompilationFacts? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return null;
        try
        {
            return JsonSerializer.Deserialize(bytes, KeptCompilationFactsJsonContext.Default.KeptCompilationFacts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static KeptCompilationFacts Of(CompilerOptions options, ModuleCompilationResult result) => new(
        CompilationOptionsFingerprint.Of(options),
        result.StaticDataEnd,
        result.InteropManifest,
        result.FunctionImports,
        result.RuntimeFeatures,
        result.EntryPointToken,
        result.SynchronousEntry,
        result.DiagnosticArtifacts?.ExceptionTypes);
}

[JsonSerializable(typeof(KeptCompilationFacts))]
[ExcludeFromCodeCoverage]
internal sealed partial class KeptCompilationFactsJsonContext : JsonSerializerContext;

/// <summary>
/// Everything of a compile's options that shapes its output, as one fingerprint: two compiles
/// with the same fingerprint and the same inputs make the same module. Output and
/// intermediate paths, logging, metrics and unit switches are left out; the WIT file is taken
/// by its content.
/// </summary>
internal static class CompilationOptionsFingerprint
{
    public static string Of(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var text = new StringBuilder();
        void Add(string name, object? value) =>
            text.Append(name).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\u001F');
        Add("input", options.EntryAssemblyPath);
        foreach (var reference in options.ReferencePaths) Add("reference", reference);
        Add("entry-type", options.EntryTypeName);
        Add("entry-method", options.EntryMethodName);
        Add("entry-token", options.EntryMethodToken);
        Add("entry-kind", options.EntryPointKind);
        foreach (var export in options.Exports) Add("export", $"{export.Name}:{export.TypeName}:{export.MethodName}");
        Add("target", options.Target);
        Add("diagnostic-trace", options.DiagnosticTracePath);
        Add("wit", options.WitPath);
        Add("wit-content", options.WitPath is { } wit && File.Exists(wit)
            ? Convert.ToHexString(XxHash128.Hash(File.ReadAllBytes(wit)))
            : null);
        Add("world", options.WitWorld);
        foreach (var source in options.SourcePaths.IsDefault ? [] : options.SourcePaths) Add("source", source);
        foreach (var (alias, target) in (options.ReferenceAssemblyAliases ?? ImmutableDictionary<string, string>.Empty)
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Add("alias", $"{alias}:{target}");
        Add("exception-map", options.EmitExceptionTypeMap);
        Add("stack-trace", options.EmitStackTrace);
        Add("javascript-boundary", options.UseJavaScriptExportBoundary);
        Add("project", options.ProjectDirectory);
        Add("path-map", options.PathMap);
        Add("structured-diagnostics", options.StructuredDiagnostics);
        Add("optimization", options.Optimization);
        Add("overrides", options.OptimizationOverrides);
        foreach (var module in options.ImportModules) Add("import-module", module);
        return Convert.ToHexString(XxHash128.Hash(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
