// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Planning;

internal interface IRuntimeMaterializationCacheDescriptorBuilder
{
    RuntimeMaterializationCacheDescriptor Build(
        RuntimeMaterializationCacheDescriptorRequest request);
}
