// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ComponentModel.Raw;

internal static class RawModuleLinkingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerRawModuleLinking(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRawModuleLinkInputValidator, RawModuleLinkInputValidator>();
        services.AddSingleton<IRawModuleLinkExecution, RawModuleLinkExecution>();
        services.AddSingleton<IRawModuleLinker, RawModuleLinker>();
        return services;
    }
}
