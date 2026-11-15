// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

// ArrayChecked: the caller has already checked the array for null, or proved it non-null.
internal readonly record struct RectangularArrayElementAddressRequest(
    InstructionEmissionRequest Instruction,
    int ArraySlot,
    bool ArrayChecked = false);
