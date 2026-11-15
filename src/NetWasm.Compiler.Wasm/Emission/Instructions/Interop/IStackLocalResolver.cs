// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IStackLocalResolver
{
    int Resolve(MethodEmissionContext context, int slot, CliValueKind type);
}
