// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include "collector_allocation.h"
#include "collector_collection.h"
#include "collector_ephemeron.h"
#include "collector_finalization.h"
#include "collector_lifecycle.h"
#include "collector_metadata.h"
#include "collector_roots.h"
#include "collector_weak_reference.h"
#include "bump/bump_arena.h"
#include "../runtime_libc_initializer.h"

#include <stdlib.h>

/* Bump chunks are zeroed when acquired and never reused, so every allocation is
 * zeroed; nothing is traced. */
/* Bump allocates from its arena, so code outside it always calls in. */
static NetWasmAllocationRun no_allocation_runs[NETWASM_ALLOCATION_RUN_CLASSES][2];
NetWasmAllocationRun *collector_allocation_runs = &no_allocation_runs[0][0];

void *collector_allocate_object(size_t bytes, bool contains_references)
{
    (void)contains_references;
    return netwasm_bump_allocate(bytes);
}

void *collector_allocate_uninitialized_object(size_t bytes)
{
    return netwasm_bump_allocate(bytes);
}

void collector_initialize(void)
{
    runtime_initialize_libc();
    netwasm_bump_initialize();
}


void collector_collect(void) { }
void collector_collect_young(void) { }
bool collector_collect_when_idle(void) { return false; }
unsigned collector_collect_when_quiet(void) { return 0; }
uint64_t collector_collection_count(void) { return 0; }
uint64_t collector_oldest_generation_collection_count(void) { return 0; }
uint32_t collector_object_generation(uintptr_t object) { (void)object; return 0; }
uint64_t collector_total_allocated_bytes(void) { return netwasm_bump_total_allocated_bytes(); }
uint64_t collector_heap_size(void) { return netwasm_bump_heap_size(); }
uint64_t collector_free_bytes(void) { return netwasm_bump_free_bytes(); }
bool collector_try_read_total_pause_milliseconds(uint64_t *value)
{
    (void)value;
    return false;
}

void *collector_allocate_metadata(size_t size)
{
    runtime_initialize_libc();
    return calloc(1, size == 0 ? 1 : size);
}

void collector_release_metadata(void *address)
{
    free(address);
}

void collector_register_roots(void *start, size_t size)
{
    (void)start;
    (void)size;
}

void collector_unregister_roots(void *start, size_t size)
{
    (void)start;
    (void)size;
}

void collector_register_root_range(void *start, void *end)
{
    (void)start;
    (void)end;
}

void collector_unregister_root_range(void *start, void *end)
{
    (void)start;
    (void)end;
}

void collector_set_root_enumerator(NetWasmCollectorRootEnumerator enumerator)
{
    (void)enumerator;
}

void collector_trace_root_range(void *start, void *end)
{
    (void)start;
    (void)end;
}

void collector_clear_roots(void) { }

int collector_register_weak_reference(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime lifetime)
{
    /* Bump never reclaims targets, so no clearing bookkeeping is required. */
    (void)slot;
    (void)target;
    (void)lifetime;
    return 1;
}

int collector_register_short_weak_reference(uintptr_t *slot, uintptr_t target)
{
    return collector_register_weak_reference(
        slot,
        target,
        NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
}

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime)
{
    (void)slot;
    (void)lifetime;
    return 0;
}

void collector_register_finalizer(
    void *object,
    NetWasmCollectorFinalizer finalizer,
    void *context,
    NetWasmCollectorFinalizer *previous_finalizer,
    void **previous_context)
{
    /* Bump never reclaims an object, so no finalizer ever becomes due: .NET runs a finalizer only after a
       collection finds its object unreachable, and none at exit. */
    (void)object;
    (void)finalizer;
    (void)context;
    if (previous_finalizer != NULL) *previous_finalizer = NULL;
    if (previous_context != NULL) *previous_context = NULL;
}

bool collector_has_pending_finalizers(void) { return false; }
int collector_invoke_finalizers(void) { return 0; }

/* Bump never reclaims a key, so an ephemeron's value simply stays with it. */
int collector_register_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    (void)key_slot;
    (void)value_slot;
    return 1;
}

void collector_unregister_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    (void)key_slot;
    (void)value_slot;
}
