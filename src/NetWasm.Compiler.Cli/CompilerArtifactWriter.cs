using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Cli;

// The files one compilation writes: the sidecars the options ask for, then the
// core module. Every check comes before the first write. Native callbacks need
// the runtime layout that names their object file.
internal sealed record CompilerArtifactWriteRequest(
    ManagedModuleCompilation Compilation,
    string CoreModulePath,
    string CallbackObjectPath)
{
    public string? RuntimeLayoutPath { get; init; }
    public string? InteropManifestPath { get; init; }
    public string? CompilerMetadataPath { get; init; }
    public string? ExceptionTypeMapPath { get; init; }
    public string? StackTraceSymbolsPath { get; init; }
}

internal interface ICompilerArtifactWriter
{
    void Write(CompilerArtifactWriteRequest request);
}

internal sealed class CompilerArtifactWriter(
    INativeCallbackSupportArtifactValidator callbackSupport,
    IBinaryFileWriter binaryFiles,
    ITextFileWriter textFiles,
    IFileDeleter files) : ICompilerArtifactWriter
{
    private readonly INativeCallbackSupportArtifactValidator _callbackSupport =
        callbackSupport ?? throw new ArgumentNullException(nameof(callbackSupport));
    private readonly IBinaryFileWriter _binaryFiles = binaryFiles ??
        throw new ArgumentNullException(nameof(binaryFiles));
    private readonly ITextFileWriter _textFiles = textFiles ??
        throw new ArgumentNullException(nameof(textFiles));
    private readonly IFileDeleter _files = files ??
        throw new ArgumentNullException(nameof(files));

    public void Write(CompilerArtifactWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var compilation = request.Compilation;
        if (compilation.NativeImports.IsDefault)
        {
            throw new InvalidOperationException("The compiler native import facts are uninitialized.");
        }
        _callbackSupport.Validate(compilation.NativeCallbackSupport);
        if (compilation.NativeCallbackSupport is not null && request.RuntimeLayoutPath is null)
        {
            throw CliOptionException.Create("native callbacks require -runtimelayout");
        }
        ValidateDistinctPaths(request);
        var exceptionTypeMap = request.ExceptionTypeMapPath is null
            ? null
            : compilation.ExceptionTypeMap ?? throw new InvalidOperationException(
                "compiler diagnostics did not produce an exception type map");
        var stackTraceSymbols = request.StackTraceSymbolsPath is null
            ? null
            : compilation.StackTraceSymbols ?? throw new InvalidOperationException(
                "stack-trace instrumentation did not produce its symbol sidecar");
        string[] runtimeFeatures = compilation.RuntimeFeatures.IsDefault
            ? []
            : [.. compilation.RuntimeFeatures.Order(StringComparer.Ordinal)];

        var layout = new CompilerRuntimeLayout(
            compilation.NativeCallbackSupport is null ? 3 : 4,
            compilation.Target,
            compilation.StaticDataEnd,
            compilation.EntryPoint,
            runtimeFeatures,
            compilation.NativeImports,
            compilation.InteropManifest.ImportModules);
        if (compilation.NativeCallbackSupport is { } callbackSupport)
        {
            _binaryFiles.Write(request.CallbackObjectPath, callbackSupport.ObjectBytes);
            layout = layout with
            {
                NativeCallbackSupport = new CompilerNativeCallbackSupport(
                    Path.GetFileName(request.CallbackObjectPath),
                    callbackSupport.Sha256,
                    callbackSupport.Callbacks,
                    callbackSupport.TemporaryApplicationExports,
                    callbackSupport.TemporaryRuntimeExports),
            };
        }
        else
        {
            _files.Delete(request.CallbackObjectPath);
        }
        WriteJson(request.RuntimeLayoutPath, layout, CompilerArtifactJsonContext.Sidecars.CompilerRuntimeLayout);
        WriteJson(
            request.InteropManifestPath,
            compilation.InteropManifest,
            CompilerArtifactJsonContext.Sidecars.HostInteropManifest);
        if (exceptionTypeMap is not null)
        {
            _binaryFiles.Write(request.ExceptionTypeMapPath!, exceptionTypeMap.Bytes);
        }
        var functionImports = compilation.FunctionImports.IsDefault ? [] : compilation.FunctionImports;
        WriteJson(
            request.CompilerMetadataPath,
            new CompilerMetadataSidecar(
                1,
                compilation.Target,
                runtimeFeatures,
                [.. functionImports.Select(import => new CompilerFunctionImportSidecar(
                    import.Module,
                    import.Name,
                    import.Type.Parameters,
                    import.Type.Result))]),
            CompilerArtifactJsonContext.Sidecars.CompilerMetadataSidecar);
        if (stackTraceSymbols is not null)
        {
            _binaryFiles.Write(request.StackTraceSymbolsPath!, stackTraceSymbols.Bytes);
        }
        // The core module is written last, so a complete module means complete sidecars.
        _binaryFiles.Write(request.CoreModulePath, compilation.CoreModule);
    }

    private void WriteJson<T>(string? path, T value, JsonTypeInfo<T> typeInfo)
    {
        if (path is not null)
        {
            _textFiles.Write(path, JsonSerializer.Serialize(value, typeInfo));
        }
    }

    // Outputs may be copied between case-sensitive and case-insensitive hosts,
    // so no two may differ only by case.
    private static void ValidateDistinctPaths(CompilerArtifactWriteRequest request)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     request.CoreModulePath,
                     request.CallbackObjectPath,
                     request.RuntimeLayoutPath,
                     request.InteropManifestPath,
                     request.CompilerMetadataPath,
                     request.ExceptionTypeMapPath,
                     request.StackTraceSymbolsPath,
                 })
        {
            if (path is not null && !distinct.Add(Path.GetFullPath(path)))
            {
                throw CliOptionException.Create("compiler output and sidecar paths must be distinct");
            }
        }
    }
}
