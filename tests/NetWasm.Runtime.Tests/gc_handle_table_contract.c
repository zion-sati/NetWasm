#include "collector/collector_metadata.h"
#include "collector/collector_pinning.h"
#include "collector/collector_roots.h"
#include "collector/collector_weak_reference.h"
#include "collector/gc_handle_table.h"

#include <assert.h>
#include <stdlib.h>

static unsigned pins;
static NetWasmWeakReferenceLifetime lifetime;

void *collector_allocate_metadata(size_t size) { return malloc(size); }
void collector_release_metadata(void *address) { free(address); }
void collector_register_roots(void *start, size_t size) { (void)start; (void)size; }
void collector_unregister_roots(void *start, size_t size) { (void)start; (void)size; }
void collector_pin_reference(uintptr_t *slot) { assert(slot != NULL); pins++; }
void collector_unpin_reference(uintptr_t *slot) { assert(slot != NULL); pins--; }

int collector_register_weak_reference(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime requested_lifetime)
{
    assert(slot != NULL);
    assert(target != 0);
    lifetime = requested_lifetime;
    return 1;
}

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime requested_lifetime)
{
    assert(slot != NULL);
    lifetime = requested_lifetime;
    return 1;
}

int main(void)
{
    NetWasmGcHandleTable table = {0};
    assert(gc_handle_table_create(&table, 1, 4) == 0);

    uint32_t weak = gc_handle_table_create(&table, 11, 0);
    assert((weak & 3u) == 0);
    assert(lifetime == NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);
    uint32_t long_weak = gc_handle_table_create(&table, 13, 1);
    assert((long_weak & 3u) == 1);
    assert(lifetime == NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION);
    uint32_t strong = gc_handle_table_create(&table, 17, 2);
    uint32_t pinned = gc_handle_table_create(&table, 19, 3);
    assert((strong & 3u) == 2);
    assert((pinned & 3u) == 3);
    assert(pins == 1);

    assert(gc_handle_table_get(&table, weak) == 11);
    assert(gc_handle_table_get(&table, long_weak) == 13);
    assert(gc_handle_table_get(&table, strong) == 17);
    assert(gc_handle_table_get(&table, pinned) == 19);

    gc_handle_table_set(&table, weak, 23);
    gc_handle_table_set(&table, strong, 29);
    gc_handle_table_set(&table, pinned, 31);
    assert(gc_handle_table_get(&table, weak) == 23);
    assert(gc_handle_table_get(&table, strong) == 29);
    assert(gc_handle_table_get(&table, pinned) == 31);
    assert(pins == 1);

    gc_handle_table_release(&table, weak);
    gc_handle_table_release(&table, long_weak);
    gc_handle_table_release(&table, strong);
    gc_handle_table_release(&table, pinned);
    assert(gc_handle_table_get(&table, weak) == 0);
    assert(gc_handle_table_get(&table, strong) == 0);
    assert(pins == 0);

    gc_handle_table_set(&table, 0, 37);
    gc_handle_table_release(&table, 0);
    assert(gc_handle_table_get(&table, 0) == 0);
    return 0;
}
