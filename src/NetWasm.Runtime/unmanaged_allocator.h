#ifndef NETWASM_UNMANAGED_ALLOCATOR_H
#define NETWASM_UNMANAGED_ALLOCATOR_H

#include <stddef.h>

/*
 * Unmanaged storage is outside the managed object graph. The selected backend
 * is not part of managed reachability and need not clear returned storage. The
 * selected allocator may reach a managed safepoint, so compiler root analysis
 * must preserve live managed values across calls into this boundary.
 * Selection is deliberately made at build time so a managed-heap replacement
 * does not change the public runtime ABI or CoreLib's NativeMemory contract.
 */
#ifndef NETWASM_UNMANAGED_ALLOCATOR_BACKEND
#define NETWASM_UNMANAGED_ALLOCATOR_BACKEND "unmanaged_allocator_bdwgc.h"
#endif

#include NETWASM_UNMANAGED_ALLOCATOR_BACKEND

#endif
