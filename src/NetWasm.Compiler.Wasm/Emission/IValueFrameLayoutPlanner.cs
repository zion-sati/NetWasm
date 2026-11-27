// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IValueFrameLayoutPlanner
{
    ValueFrameLayout Create(StructuredMethod method, NativeImportPlan? nativeImports = null,
        MemberExecutionPlan? memberExecution = null,
        ManagedByRefMethodEffects? byRefEffects = null,
        AddressTakenScalarPromotionPlan? addressTakenScalarPromotions = null,
        AddressTakenAggregatePromotionPlan? addressTakenAggregatePromotions = null,
        ScalarCallLifetimePlan? scalarCallLifetimes = null,
        ReturnBufferLocal? returnBufferLocal = null,
        StructConventionStorage? structConventions = null);
}

// Where the struct conventions keep values out of the frame or put them in it: locals held in lanes, calls whose
// results stay in lanes, whole loads a call or ret takes from lanes or parameters (no temporary), struct arguments
// passed by their fields that keep a frame copy, and those read in place from their parameters (none).
internal sealed record StructConventionStorage(
    System.Collections.Generic.IReadOnlySet<int> LaneLocals,
    System.Collections.Generic.IReadOnlySet<int> LaneCalls,
    System.Collections.Generic.IReadOnlySet<int> LaneLoads,
    System.Collections.Generic.IReadOnlySet<int> CopiedArguments,
    System.Collections.Generic.IReadOnlySet<int> InPlaceArguments);
