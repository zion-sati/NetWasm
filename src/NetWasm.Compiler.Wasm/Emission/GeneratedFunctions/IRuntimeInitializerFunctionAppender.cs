// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IRuntimeInitializerFunctionAppender
{
    /// <summary>Appends the module's runtime initializer and returns its function index.</summary>
    int Append(IList<WasmFunctionDefinition> functions, int importCount, RuntimeInitializationPlan plan);
}
