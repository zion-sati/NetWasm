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
            // Native callbacks need schema 4 and their object file, which this host does not write.
            if (result.NativeCallbackSupport is not null)
                throw new InvalidOperationException("the compiler host writes no native callback support");
            // The layout nwc writes (schema 3): the runtime features the program uses, so the linked test runtime
            // keeps what the program's own build would, and the reached native imports (the C library functions
            // CoreLib's Math and MathF import, which the runtime exports; eng/build-netwasm-runtime.sh).
            Add(request.RuntimeLayoutPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 3,
                target,
                applicationStaticDataEnd = result.StaticDataEnd,
                managedExecutableEntryPoint = (object?)null,
                runtimeFeatures = result.RuntimeFeatures.Order(StringComparer.Ordinal).ToArray(),
                importModules = Array.Empty<string>(),
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
