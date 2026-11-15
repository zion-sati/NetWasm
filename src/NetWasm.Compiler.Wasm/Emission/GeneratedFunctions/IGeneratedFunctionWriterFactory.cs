// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

/// <summary>
/// Creates an isolated writer for one generated function body.
/// </summary>
internal interface IGeneratedFunctionWriterFactory
{
    GeneratedFunctionWriterLease Create();
}
