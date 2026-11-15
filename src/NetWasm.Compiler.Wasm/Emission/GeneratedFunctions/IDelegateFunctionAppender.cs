// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateFunctionAppender
{
    void Append(IList<WasmFunctionDefinition> functions,
        ImmutableArray<MethodInstanceModel> invokes,
        DelegateInvokeTarget invokeTarget,
        FunctionIndexMap indices,
        IFunctionIndexResolver functionIndices);
}
