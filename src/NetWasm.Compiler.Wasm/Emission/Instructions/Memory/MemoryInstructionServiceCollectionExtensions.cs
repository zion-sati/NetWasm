// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

internal static class MemoryInstructionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmMemoryInstructions(
        this IServiceCollection services)
    {
        services.AddSingleton<IStaticInitializerGuardResolver, StaticInitializerGuardResolver>();
        services.AddSingleton<IStaticInitializationEmitter, StaticInitializationEmitter>();
        services.AddSingleton<IMethodInitializationTriggerClassifier, MethodInitializationTriggerClassifier>();
        services.AddSingleton<IStaticFieldInitializationClassifier, StaticFieldInitializationClassifier>();
        services.AddSingleton<IReferenceWriteBarrierEmitter, ReferenceWriteBarrierEmitter>();
        services.AddSingleton<INullDereferenceCheckEmitter, NullDereferenceCheckEmitter>();
        services.AddSingleton<IReferenceWriteRecordPolicy, ReferenceWriteRecordPolicy>();
        services.AddSingleton<IReferenceWriteAddressRememberer, ReferenceWriteAddressRememberer>();
        services.AddInstructionCommandProvider<ValueObjectBlockMemoryEmitter>();
        services.AddInstructionCommandProvider<FieldInstructionEmitter>();
        services.AddInstructionCommandProvider<StaticFieldInstructionEmitter>();
        services.AddInstructionCommandProvider<ReadOnlySpanInstructionEmitter>();
        services.AddInstructionCommandProvider<StaticDataElementInstructionEmitter>();
        services.AddInstructionCommandProvider<AtomicInstructionEmitter>();
        return services;
    }
}
