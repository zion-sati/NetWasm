// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Testing.CompilerHost;

internal sealed record CompilerHostArtifact(string Path, ImmutableArray<byte> Bytes);

internal interface ICompilerHostArtifactFormatter
{
    ImmutableArray<CompilerHostArtifact> Format(CompilationRequest request, CompilationResult result);
}

internal sealed class CompilerHostArtifactFormatter : ICompilerHostArtifactFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public ImmutableArray<CompilerHostArtifact> Format(CompilationRequest request, CompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        var artifacts = ImmutableArray.CreateBuilder<CompilerHostArtifact>();
        Add(request.ModulePath, result.ApplicationModule);
        if (request.EmitStackTrace)
        {
            Add(request.MethodSymbolsPath, result.MethodSymbols?.Bytes ??
                throw new InvalidOperationException("instrumented compilation produced no stack-trace sidecar"));
        }
        if (request.RuntimeLayoutPath is not null)
        {
            var target = request.Target switch
            {
                WasmTarget.Wasm32 => "wasm32",
                WasmTarget.Wasm64 => "wasm64",
                _ => throw new ArgumentOutOfRangeException(nameof(request), "unsupported runtime target"),
            };
            if (result.StaticDataEnd < 0)
                throw new InvalidOperationException("application static-data extent is negative");
            // The reached native imports, as nwc writes them: the C library functions CoreLib's Math and MathF
            // import are among them, and a linked test runtime exports those (eng/build-netwasm-runtime.sh).
            Add(request.RuntimeLayoutPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 2,
                target,
                applicationStaticDataEnd = result.StaticDataEnd,
                nativeImports = result.NativeImports.Select(static import => new
                {
                    libraryName = import.LibraryName,
                    entryPoint = import.EntryPoint,
                    parameters = import.Parameters.Select(ValueTypeName).ToArray(),
                    returnType = import.ReturnType is { } returnType ? ValueTypeName(returnType) : null,
                }).ToArray(),
            }, JsonOptions));
        }
        if (request.InteropManifestPath is not null)
        {
            ArgumentNullException.ThrowIfNull(result.InteropManifest);
            Add(request.InteropManifestPath, JsonSerializer.SerializeToUtf8Bytes(result.InteropManifest, JsonOptions));
        }
        return artifacts.ToImmutable();

        static string ValueTypeName(WasmValueType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

        void Add(string? path, byte[] bytes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(bytes);
            if (artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Path, path)))
                throw new ArgumentException("compiler artifact paths must be distinct", nameof(request));
            artifacts.Add(new(path, [.. bytes]));
        }
    }
}
