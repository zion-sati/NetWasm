// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IEmscriptenEnvironmentShimWriter
{
    void Write(string outputPath, ComponentTarget target);
}

public sealed class EmscriptenEnvironmentShimWriter(
    IWasmTextModuleWriter modules) : IEmscriptenEnvironmentShimWriter
{
    private readonly IWasmTextModuleWriter _modules = modules ??
        throw new ArgumentNullException(nameof(modules));

    public void Write(string outputPath, ComponentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var addressType = target.Width == "wasm64" ? "i64" : "i32";
        _modules.Write(
            "(module " +
            $"(func (export \"emscripten_notify_memory_growth\") (param {addressType})))",
            outputPath);
    }
}
