// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeEqualsIntrinsicEmitter(
    IValueTypeEqualsEmitter equals) : IRuntimeIntrinsicEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var type = request.ConstrainedType ??
            throw new InvalidOperationException(
                "direct value type equality requires its constrained type");
        equals.Emit(request, code, type, CliValueKind.ManagedAddress);
    }
}
