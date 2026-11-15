// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IModuleDataPlanner
{
    ModuleDataPlan Build(
        IEnumerable<StructuredMethodEmission> methods,
        IReadOnlyList<EntityKey> directInitializers,
        IReadOnlyList<string> constructedInitializers,
        bool reserveNativeCallbackReadiness,
        SidecarDescriptor? sidecars = null,
        bool reserveStateRegistrationGuard = false);
}
