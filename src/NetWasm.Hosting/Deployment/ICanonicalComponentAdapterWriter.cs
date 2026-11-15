// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Deployment;

/// <summary>Emits the deterministic ESM boundary for one generated component.</summary>
public interface ICanonicalComponentAdapterWriter
{
    byte[] Write(CanonicalComponentAdapterRequest request);
}
