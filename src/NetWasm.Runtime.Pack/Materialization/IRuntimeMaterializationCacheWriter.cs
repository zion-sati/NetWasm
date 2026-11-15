// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeMaterializationCacheWriter
{
    void Write(
        string cacheDirectory,
        RuntimeMaterializationCacheSlot slot,
        RuntimeMaterializationCacheKey key,
        byte[] bytes,
        string sha256,
        RuntimeNativeCacheEvidence? nativeEvidence = null);
}
