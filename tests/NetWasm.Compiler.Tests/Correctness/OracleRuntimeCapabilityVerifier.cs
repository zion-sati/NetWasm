// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class OracleRuntimeCapabilityVerifier : IOracleRuntimeCapabilityVerifier
{
    void IOracleRuntimeCapabilityVerifier.Verify(
        OracleRuntimeCapabilities required, OracleRuntimeCapabilities provided)
    {
        var missing = required & ~provided;
        if (missing != OracleRuntimeCapabilities.None)
        {
            throw new InvalidOperationException($"Oracle runtime lacks required capabilities: {missing}.");
        }
    }
}
