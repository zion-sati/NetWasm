// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_UNMANAGED_ALLOCATOR_H
#define NETWASM_UNMANAGED_ALLOCATOR_H

#include <stddef.h>

/*
 * Unmanaged storage is outside the managed object graph. It comes from libc,
 * is not part of managed reachability and need not clear returned storage.
 * The allocator may reach a managed safepoint, so compiler root analysis must
 * preserve live managed values across calls into this boundary.
 */
#include "unmanaged_allocator_libc.h"

#endif
