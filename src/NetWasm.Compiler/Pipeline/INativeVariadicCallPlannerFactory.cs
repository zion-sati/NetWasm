// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Pipeline;

internal interface INativeVariadicCallPlannerFactory
{
    // The planner for one compile: the archives it links give C variadic functions their types.
    INativeVariadicCallPlanner Create(CompilerOptions options);
}
