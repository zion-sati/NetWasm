// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal interface IWasmEmissionInvariantValidator
{
    void Validate(ReadOnlySpan<byte> bytes, WasmTarget target);
}
