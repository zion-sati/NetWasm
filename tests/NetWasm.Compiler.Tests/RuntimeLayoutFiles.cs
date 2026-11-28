// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tests;

// The runtime layout nwc writes for a compilation (schema 3), for tests that build the native runtime for a program
// themselves (eng/build-netwasm-runtime.sh): the program's runtime features, so the runtime keeps what the program's
// own build would, and its reached native imports.
internal static class RuntimeLayoutFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Write(string path, CompilationResult result, WasmTarget target = WasmTarget.Wasm32)
    {
        ArgumentNullException.ThrowIfNull(result);
        // Native callbacks need schema 4 and their object file, which these tests do not build.
        if (result.NativeCallbackSupport is not null)
            throw new InvalidOperationException("the test runtime layout has no native callback support");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 3,
            target = target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
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

    private static string ValueTypeName(WasmValueType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());
}
