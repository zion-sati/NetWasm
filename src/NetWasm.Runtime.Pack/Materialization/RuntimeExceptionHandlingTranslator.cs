// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

// Rewrites a linked module's legacy exception handling (try, catch, delegate, rethrow) as exnref (try_table,
// throw_ref), the encoding the profile allows and Wasmtime runs. Binaryen keeps the module's names and every other
// instruction; it runs at every optimization level, since an unoptimized module needs it as much.
internal sealed class RuntimeExceptionHandlingTranslator(ICommandInvoker commands) : IRuntimeExceptionHandlingTranslator
{
    private readonly ICommandInvoker _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    public void Translate(RuntimeExceptionHandlingTranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WasmOptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LogPath);
        if (!Path.IsPathFullyQualified(request.ModulePath))
            throw new InvalidOperationException("Exception-handling translation requires a fully qualified module path.");
        _commands.Invoke(new(request.WasmOptPath,
        [
            request.ModulePath,
            "--translate-to-exnref",
            .. RuntimeOptimizationArgumentBuilder.FeatureArguments(request.Features.Add("legacy-exceptions")),
            "-g",
            "-o",
            request.ModulePath,
        ], request.LogPath));
    }
}
