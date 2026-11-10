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

void collector_initialize(void)
{
    runtime_initialize_libc();
    netwasm_bump_initialize();
}


void collector_collect(void) { }
uint64_t collector_collection_count(void) { return 0; }
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
    (void)object;
    (void)finalizer;
    (void)context;
    (void)previous_finalizer;
    (void)previous_context;
    abort();
}

bool collector_has_pending_finalizers(void) { return false; }
int collector_invoke_finalizers(void) { return 0; }

int collector_register_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    (void)key_slot;
    (void)value_slot;
    abort();
}

void collector_unregister_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    (void)key_slot;
    (void)value_slot;
    abort();
}
