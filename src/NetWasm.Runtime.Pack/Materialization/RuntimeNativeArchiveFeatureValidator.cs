// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Linq;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

// A library whose code uses a Wasm feature outside the static-native profile,
// such as tail calls or threads, fails here with its name, the profile and the
// validator's reason, before the whole module's validation could only report
// that a tool failed. Wasm exception handling in its legacy encoding, which
// Emscripten 3 and 4 emit for setjmp/longjmp and C++ exceptions, is accepted and
// reported, so the final link translates it to the profile's exnref encoding.
// An archive whose code throws or catches C++ exceptions is reported too, so the
// link takes the C++ libraries built with Wasm exception handling.
internal sealed class RuntimeNativeArchiveFeatureValidator(
    IRuntimeNativeModuleValidator modules,
    IRuntimeArtifactReader artifacts) : IRuntimeNativeArchiveFeatureValidator
{
    private readonly IRuntimeNativeModuleValidator _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    private readonly IRuntimeArtifactReader _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    private const string LegacyExceptions = "legacy-exceptions";

    public RuntimeNativeArchiveFeatures Validate(RuntimeNativeArchiveFeatureValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LibraryName);
        ArgumentNullException.ThrowIfNull(request.Module);
        try
        {
            _modules.Validate(request.Module);
            return new(UsesLegacyExceptions: false, UsesCxxExceptions(request.Module.Path));
        }
        catch (InvalidOperationException) when (!request.Module.Profile.Features.Contains(LegacyExceptions))
        {
        }
        var legacy = request.Module with
        {
            Profile = request.Module.Profile with { Features = request.Module.Profile.Features.Add(LegacyExceptions) },
            LogPath = request.Module.LogPath + ".legacy-exceptions.log",
        };
        try
        {
            _modules.Validate(legacy);
            return new(UsesLegacyExceptions: true, UsesCxxExceptions(request.Module.Path));
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Native library '{request.LibraryName}' uses WebAssembly beyond NetWasm's static native profile " +
                $"({string.Join(", ", request.Module.Profile.Features)}): {Reason(request.Module.LogPath)}. " +
                "Build it without that feature.", exception);
        }
    }

    // C++ code that throws, rethrows or catches calls the C++ ABI's exception functions, and Wasm exception handling's
    // personality; in the archive's relocatable link, each one it does not define is a function import. Code that
    // throws or catches with the C++ exception tag itself imports the tag, which the C++ exception closure defines.
    private static readonly FrozenSet<string> CxxExceptionFunctions = new[]
    {
        "__cxa_throw", "__cxa_rethrow", "__cxa_begin_catch", "__cxa_end_catch", "_Unwind_CallPersonality",
    }.ToFrozenSet(StringComparer.Ordinal);

    private bool UsesCxxExceptions(string objectPath)
    {
        var module = _artifacts.Read(objectPath);
        return WasmImportSection.ReadFunctionImports(module).Any(import => CxxExceptionFunctions.Contains(import.Name)) ||
            WasmImportSection.ReadTagImports(module).Any(import => import.Name == "__cpp_exception");
    }

    // wasm-tools names the missing feature under "Caused by:", after the function that failed.
    private string Reason(string logPath)
    {
        var lines = Encoding.UTF8.GetString(_artifacts.Read(logPath))
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cause = lines.SkipWhile(line => line != "Caused by:").Skip(1).FirstOrDefault();
        if (cause is null) return lines.FirstOrDefault() ?? "the validator gave no reason";
        // The cause is numbered: "0: SIMD support is not enabled (at offset 0x103b)".
        var separator = cause.IndexOf(": ", StringComparison.Ordinal);
        return separator < 0 ? cause : cause[(separator + 2)..];
    }
}
