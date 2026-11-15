// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class OptionalFunctionIndexValidator :
    IOptionalFunctionIndexValidator
{
    public void Validate(OptionalFunctionIndex index, string message)
    {
        if (!index.IsPresent)
        {
            throw new InvalidOperationException(message);
        }
    }
}
