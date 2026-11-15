// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include "collector_pinning.h"

static uint64_t pinned_references;

void collector_pin_reference(uintptr_t *slot)
{
    (void)slot;
    if (pinned_references != UINT64_MAX) pinned_references++;
}

void collector_unpin_reference(uintptr_t *slot)
{
    (void)slot;
    if (pinned_references != 0) pinned_references--;
}

uint64_t collector_pinned_reference_count(void)
{
    return pinned_references;
}
