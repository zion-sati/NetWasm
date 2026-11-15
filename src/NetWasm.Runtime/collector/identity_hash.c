// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include "collector_identity_hash.h"

uint32_t collector_identity_hash(netwasm_reference_t object)
{
    uint64_t value = (uint64_t)object;
    value ^= value >> 16;
    value *= UINT64_C(0x45d9f3b);
    value ^= value >> 16;
    return (uint32_t)value;
}
