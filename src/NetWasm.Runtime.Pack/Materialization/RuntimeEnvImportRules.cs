// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

// What a project may declare as <NetWasmEnvImport>: a function name the runtime's own env does not use, supplied
// through an import module the project declares, for raw WebAssembly output, whose JavaScript supplies it.
internal static class RuntimeEnvImportRules
{
    public static ImmutableArray<RuntimeEnvImport> Validate(
        ImmutableArray<RuntimeEnvImport> imports,
        bool rawWasmOutput,
        IReadOnlySet<string>? importModules,
        IReadOnlySet<string> runtimeEnvFunctions)
    {
        ArgumentNullException.ThrowIfNull(runtimeEnvFunctions);
        if (imports.IsDefaultOrEmpty)
            return [];
        if (!rawWasmOutput)
            throw new InvalidOperationException(
                "NetWasmEnvImport applies to JsModule and raw WebAssembly output, whose JavaScript supplies the " +
                "functions. A component has no JavaScript: it links NetWasm's stand-ins for Emscripten's GL functions, " +
                "or a native library's own definitions.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (import is null || !IsSymbol(import.Name))
                throw new InvalidOperationException(
                    $"NetWasmEnvImport '{import?.Name}' is not a C function name.");
            if (!names.Add(import.Name))
                throw new InvalidOperationException($"NetWasmEnvImport '{import.Name}' is declared twice.");
            if (runtimeEnvFunctions.Contains(import.Name))
                throw new InvalidOperationException(
                    $"NetWasmEnvImport '{import.Name}' is one of the NetWasm runtime's own env functions.");
            if (string.IsNullOrWhiteSpace(import.Module) || import.Module == "env" ||
                import.Module.StartsWith("netwasm", StringComparison.Ordinal) ||
                import.Module.StartsWith("wasi", StringComparison.Ordinal) || import.Module.Contains('|'))
                throw new InvalidOperationException(
                    $"NetWasmEnvImport '{import.Name}' needs Module, the project's import module that supplies it " +
                    "(not env, which is the runtime's, nor a NetWasm or WASI module).");
            if (importModules?.Contains(import.Module) != true)
                throw new InvalidOperationException(
                    $"NetWasmEnvImport '{import.Name}' names module '{import.Module}', which the project does not " +
                    $"declare. Declare <NetWasmImportModule Include=\"{import.Module}\" />, or the NetWasmJavaScriptModule " +
                    "that supplies it.");
        }
        return [.. imports.OrderBy(import => import.Name, StringComparer.Ordinal)];
    }

    private static bool IsSymbol(string? name) =>
        !string.IsNullOrEmpty(name) && (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '$' or '.');
}
