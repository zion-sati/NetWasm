// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Pipeline;

internal sealed class NativeVariadicCallPlannerFactory(INativeArchiveFunctionTypeReader archives)
    : INativeVariadicCallPlannerFactory
{
    private readonly INativeArchiveFunctionTypeReader _archives = archives ?? throw new ArgumentNullException(nameof(archives));

    public INativeVariadicCallPlanner Create(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.NativeArchives.IsDefaultOrEmpty
            ? NoNativeVariadicCalls.Instance
            : new NativeVariadicCallPlanner(_archives, options.NativeArchives, options.Target == WasmTarget.Wasm64);
    }
}
