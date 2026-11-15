// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

[Flags]
internal enum OracleRuntimeCapabilities
{
    None = 0,
    GarbageCollection = 1,
    Finalization = 2,
    WeakReferenceClearing = 4,
}
