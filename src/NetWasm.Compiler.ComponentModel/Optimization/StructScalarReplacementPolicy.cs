// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum StructScalarReplacementPolicy
{
    Disabled,
    // A struct local whose address is only taken to reach its fields becomes
    // a local per field, unless whole copies into or out of it run as often
    // as its fields are accessed.
    FieldAccessedLocals,
}
