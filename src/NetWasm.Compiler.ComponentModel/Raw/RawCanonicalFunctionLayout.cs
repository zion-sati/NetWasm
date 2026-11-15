// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawCanonicalFunctionLayout(
    WasmTarget Target,
    CanonicalAbiFunction Function,
    string Module,
    string Name,
    CanonicalAbiCoreSignature Signature,
    CanonicalAbiMemoryLayout ParameterMemory,
    CanonicalAbiMemoryLayout? ResultMemory);
