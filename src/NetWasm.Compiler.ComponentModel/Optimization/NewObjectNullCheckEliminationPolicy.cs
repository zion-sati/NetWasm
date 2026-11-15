// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum NewObjectNullCheckEliminationPolicy
{
    Disabled,
    // A field access or callvirt on an object newobj just produced is not null checked.
    NewObjects,
    // Nor is one on the this of a constructor that only newobj and chained constructors call.
    ConstructedObjects,
}
