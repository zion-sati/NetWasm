using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedMethodEmitter
{
    ManagedMethodEmission Emit(
        MethodDefinitionModel method,
        ManagedMethodIdentity callerIdentity,
        StructuredMethod structured,
        MethodRootMap rootMap,
        MethodInstanceModel? methodInstance,
        int stackTraceMethodId,
        RuntimeImportSelection runtimeImportSelection,
        Action<IWasmInstructionWriter, StructuredMethod, MethodEmissionContext> emitBody,
        NativeImportPlan? nativeImports = null,
        MemberExecutionPlan? memberExecution = null,
        ManagedByRefMethodEffects? byRefEffects = null,
        AddressTakenScalarPromotionPlan? addressTakenScalarPromotions = null,
        AddressTakenAggregatePromotionPlan? addressTakenAggregatePromotions = null,
        ManagedByRefAggregatePromotionPlan? managedByRefAggregatePromotions = null,
        ManagedAggregateSsaPlan? managedAggregateValues = null,
        bool enableBranchValueFramePlacement = false,
        bool enableArrayMetadataCaching = false,
        bool enableScalarCallPreservation = false,
        bool hoistArrayNullChecks = false,
        LoopRotationPolicy loopRotation = LoopRotationPolicy.Disabled,
        bool eliminateArrayStoreChecks = false,
        OptionalFunctionIndex managedThrowFunction = default,
        bool inlineSmallBlocks = false);
}
