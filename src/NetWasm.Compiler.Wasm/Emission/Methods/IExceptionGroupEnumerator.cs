// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IExceptionGroupEnumerator
{
    IEnumerable<StructuredExceptionGroupId> Enumerate(StructuredMethod method);
}
