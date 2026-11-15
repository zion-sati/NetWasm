// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Analysis.Attributes;

internal static class AttributeAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddAttributeAnalysis(this IServiceCollection services) =>
        services.AddSingleton<IAttributeQueryMethodSpecializerFactory, AttributeQueryMethodSpecializerFactory>();
}
