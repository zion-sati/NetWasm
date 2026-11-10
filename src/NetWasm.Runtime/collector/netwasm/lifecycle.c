/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_allocation.h"
#include "../collector_lifecycle.h"
#include "../../runtime_libc_initializer.h"
#include "runtime_state.h"

#include <stdlib.h>

/* Compact is built for size: it collects after less growth, so small heaps
 * stay small. */
#ifdef NETWASM_GC_COMPACT
#define NETWASM_GC_MINIMUM_COLLECTION_BUDGET (1u * 1024u * 1024u)
#else
#define NETWASM_GC_MINIMUM_COLLECTION_BUDGET (4u * 1024u * 1024u)
#endif

NetWasmGc *netwasm_gc_runtime;

_Static_assert(sizeof(NetWasmGcRun) == sizeof(NetWasmAllocationRun) &&
                   offsetof(NetWasmGcRun, cursor) == offsetof(NetWasmAllocationRun, cursor) &&
                   offsetof(NetWasmGcRun, end) == offsetof(NetWasmAllocationRun, end),
               "NetWasmGC's runs are the runtime's allocation runs");

static void *allocate_memory(size_t bytes, size_t alignment)
{
    (void)alignment;
    runtime_initialize_libc();
    return malloc(bytes == 0 ? 1 : bytes);
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    free(address);
}

void collector_initialize(void)
{
    if (netwasm_gc_runtime != NULL) return;
    if (netwasm_gc_create(
            allocate_memory,
            release_memory,
            NETWASM_GC_MINIMUM_COLLECTION_BUDGET,
            &netwasm_gc_runtime) != NETWASM_GC_OK) abort();
    netwasm_gc_set_root_tracer(netwasm_gc_runtime, netwasm_gc_trace_registered_roots, NULL);
#ifndef NETWASM_GC_STRESS
    /* Stress collects on every allocation, which only the allocator's slow path does. */
    NetWasmGcRun *runs = netwasm_gc_allocation_runs(netwasm_gc_runtime);
    if (runs != NULL) collector_allocation_runs = (NetWasmAllocationRun *)runs;
#endif
    netwasm_gc_lifetime_initialize(netwasm_gc_runtime);
}
