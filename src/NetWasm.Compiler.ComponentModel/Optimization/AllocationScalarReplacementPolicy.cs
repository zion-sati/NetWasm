// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum AllocationScalarReplacementPolicy
{
    Disabled,
    // An object a method allocates and only reaches through locals, to read
    // and write its fields, becomes a local per field.
    NonEscapingObjects,
}
