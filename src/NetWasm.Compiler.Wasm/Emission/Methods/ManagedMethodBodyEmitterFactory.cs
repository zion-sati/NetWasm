// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodBodyEmitterFactory(
    IManagedMethodEmitter managedMethods,
    IManagedMethodSequenceEmitter sequences,
    IStackTraceMethodIdProvider stackTraceMethodIds,
    IManagedHeapSampler heapSampler,
    IAddressTakenScalarPromotionPlanner addressTakenScalarPromotions,
    IAddressTakenAggregatePromotionPlanner addressTakenAggregatePromotions,
    IManagedByRefAggregatePromotionPlanner managedByRefAggregatePromotions,
    IManagedMethodValuePlanBuilder managedValues,
    ILogger<WasmModuleEmitterFactory> logger) : IManagedMethodBodyEmitterFactory
{
    public IManagedMethodBodyEmitter Create() =>
        new ManagedMethodBodyDiagnosticDecorator(
            new ManagedMethodBodyEmitter(
                managedMethods,
                sequences,
                stackTraceMethodIds,
                heapSampler,
                addressTakenScalarPromotions,
                addressTakenAggregatePromotions,
                managedByRefAggregatePromotions,
                managedValues),
            logger);
}
