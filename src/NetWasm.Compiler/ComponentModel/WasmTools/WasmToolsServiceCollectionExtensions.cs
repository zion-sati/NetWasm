// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ComponentModel.WasmTools;

internal static class WasmToolsServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelWasmTools(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWasmTools, ProcessWasmTools>();
        services.AddSingleton<IWasmTextModuleWriter, WasmTextModuleWriter>();
        services.AddSingleton<IEmscriptenEnvironmentShimWriter,
            EmscriptenEnvironmentShimWriter>();
        return services;
    }
}
