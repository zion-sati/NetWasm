// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed record CallEmissionRequest(
    InstructionEmissionRequest Instruction,
    MethodInstanceModel Method,
    int ArgumentBase,
    int Consumed,
    CliTypeIdentity? ConstrainedType = null);
