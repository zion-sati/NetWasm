/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_allocation.h"
#include "runtime_state.h"

#include <stdlib.h>

static __attribute__((noinline)) void *allocate_slow(size_t bytes, bool contains_references)
{
    void *result = NULL;
    NetWasmGcResult status = netwasm_gc_allocate(
        netwasm_gc_runtime, bytes, contains_references, &result);
    if (status == NETWASM_GC_INVALID_ARGUMENT) abort();
    return status == NETWASM_GC_OK ? result : NULL;
}

void *collector_allocate_object(size_t bytes, bool contains_references)
{
    /* Stress collects in the slow path, so every allocation takes it. */
#if defined(NETWASM_GC_COMPACT) || defined(NETWASM_GC_STRESS)
    return allocate_slow(bytes, contains_references);
#else
    void *result = netwasm_gc_try_allocate_available(
        netwasm_gc_runtime, bytes, contains_references);
    return result != NULL ? result : allocate_slow(bytes, contains_references);
#endif
}
