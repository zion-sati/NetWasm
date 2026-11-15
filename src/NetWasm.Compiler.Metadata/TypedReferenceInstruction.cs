// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Metadata;

// The typed-reference instructions, each a call of one of TypedReference's helpers.
internal enum TypedReferenceInstruction
{
    // mkrefany T: TypedReference.NetWasmMake<T>(ref T).
    MakeReference,

    // refanyval T: TypedReference.NetWasmValue<T>(TypedReference), which returns ref T.
    ReferenceValue,

    // refanytype: TypedReference.NetWasmTargetType(TypedReference).
    ReferenceType,
}
