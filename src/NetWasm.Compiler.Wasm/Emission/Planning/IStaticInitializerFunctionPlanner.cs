// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStaticInitializerFunctionPlanner
{
    ModuleDataPlan Build(WasmEmissionRequest request, WasmModulePlan functions, ModuleDataPlan data);
}
