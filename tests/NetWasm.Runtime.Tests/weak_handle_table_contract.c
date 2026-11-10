#include "collector/collector_metadata.h"
#include "collector/collector_weak_reference.h"
#include "collector/weak_handle_table.h"

#include <assert.h>
#include <stdlib.h>
#include <time.h>

static uintptr_t *registered_slot;
static NetWasmWeakReferenceLifetime registered_lifetime;
static unsigned registrations;
static unsigned unregistrations;

void *collector_allocate_metadata(size_t size)
{
    return malloc(size);
}

void collector_release_metadata(void *address)
{
    free(address);
}

int collector_register_weak_reference(
    uintptr_t *slot,
    uintptr_t target,
    NetWasmWeakReferenceLifetime lifetime)
{
    assert(slot != NULL);
    assert(target != 0);
    registered_slot = slot;
    registered_lifetime = lifetime;
    registrations++;
    return true;
}

int collector_unregister_weak_reference(
    uintptr_t *slot,
    NetWasmWeakReferenceLifetime lifetime)
{
    assert(slot != NULL);
    registered_lifetime = lifetime;
    unregistrations++;
    if (registered_slot == slot) registered_slot = NULL;
    return 1;
}

/* A released handle is reused at once, so creating one costs the same however many live;
 * scanning for a free slot made creating n handles quadratic. */
static void creation_cost_does_not_grow_with_live_handles(void)
{
    enum { COUNT = 100000 };
    NetWasmWeakHandleTable table = {0};
    clock_t start = clock();
    for (uint32_t handle = 1; handle <= COUNT; handle++)
        assert(weak_handle_table_create(&table, 0, false) == handle);
    for (uint32_t handle = COUNT; handle != 0; handle--) {
        weak_handle_table_release(&table, handle);
        assert(weak_handle_table_create(&table, 0, false) == handle);
    }
    assert(clock() - start < CLOCKS_PER_SEC);
}

int main(void)
{
    NetWasmWeakHandleTable table = {0};

    uint32_t null_handle = weak_handle_table_create(&table, 0, false);
    uint32_t short_handle = weak_handle_table_create(&table, 41, false);
    assert(null_handle == 1);
    assert(short_handle == 2);
    assert(weak_handle_table_get(&table, null_handle) == 0);
    assert(weak_handle_table_get(&table, short_handle) == 41);
    assert(registered_lifetime == NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION);

    *registered_slot = 0;
    assert(weak_handle_table_get(&table, short_handle) == 0);
    uint32_t long_handle = weak_handle_table_create(&table, 73, true);
    assert(long_handle == 3);
    assert(registered_lifetime == NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION);

    unsigned before = registrations;
    weak_handle_table_set(&table, long_handle, 89);
    assert(weak_handle_table_get(&table, long_handle) == 89);
    assert(registrations == before + 1);
    weak_handle_table_set(&table, long_handle, 0);
    assert(weak_handle_table_get(&table, long_handle) == 0);

    weak_handle_table_release(&table, null_handle);
    uint32_t reused_handle = weak_handle_table_create(&table, 97, false);
    assert(reused_handle == null_handle);
    weak_handle_table_release(&table, reused_handle);
    assert(weak_handle_table_get(&table, reused_handle) == 0);

    before = registrations;
    weak_handle_table_set(&table, 0, 101);
    weak_handle_table_set(&table, table.capacity + 1, 101);
    weak_handle_table_set(&table, reused_handle, 101);
    weak_handle_table_release(&table, 0);
    weak_handle_table_release(&table, table.capacity + 1);
    weak_handle_table_release(&table, reused_handle);
    assert(weak_handle_table_get(&table, 0) == 0);
    assert(weak_handle_table_get(&table, table.capacity + 1) == 0);
    assert(registrations == before);
    assert(unregistrations > 0);
    creation_cost_does_not_grow_with_live_handles();
    return 0;
}
