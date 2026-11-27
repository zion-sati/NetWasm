// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal static class CallLifetimeServiceCollectionExtensions
{
    public static IServiceCollection AddScalarCallPreservation(this IServiceCollection services)
    {
        services.AddSingleton<IScalarCallLifetimePlanner>(_ => new ScalarCallLifetimePlanner(
            new ControlFlowGraphBuilderFactory().Create(),
            new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()))));
        services.AddSingleton<IScalarCallPreservationEmitter, ScalarCallPreservationEmitter>();
        return services;
    }
}
