// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow;

public static class ControlFlowServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerControlFlow(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddStackTypeCompatibility();
        services.AddSingleton<IReachableSetOverlapClassifier,
            ReachableSetOverlapClassifier>();
        services.AddSingleton<IDispatcherBoundaryClipper,
            DispatcherBoundaryClipper>();
        services.AddSingleton<ITypedStackValidatorFactory, TypedStackValidatorFactory>();
        services.AddSingleton<IControlFlowGraphBuilderFactory, ControlFlowGraphBuilderFactory>();
        services.AddSingleton<IControlFlowGraphAnalyzerFactory, ControlFlowGraphAnalyzerFactory>();
        services.AddSingleton<IValidatedStructuredMethodBuilderFactory,
            ValidatedStructuredMethodBuilderFactory>();
        services.AddSingleton<IStructuredMethodValidatorFactory,
            StructuredMethodValidatorFactory>();
        services.AddSingleton<IStructuredMethodFactory>(static provider =>
            new StructuredMethodFactory(
                provider.GetRequiredService<IStructuredMethodValidatorFactory>().Create()));
        return services;
    }
}
