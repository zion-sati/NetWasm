// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal interface IScalarCallLifetimePlanner
{
    ScalarCallLifetimePlan Plan(StructuredMethod method, bool enabled);
}
