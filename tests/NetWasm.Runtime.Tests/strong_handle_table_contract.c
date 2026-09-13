#include "collector/collector_metadata.h"
#include "collector/collector_roots.h"
#include "collector/strong_handle_table.h"

#include <assert.h>
#include <stdlib.h>

static unsigned root_registrations;
static unsigned root_unregistrations;
static unsigned pins;
static unsigned unpins;

void *collector_allocate_metadata(size_t size)
{
    return malloc(size);
}

void collector_release_metadata(void *address)
{
    free(address);
}

void collector_register_roots(void *start, size_t size)
{
    assert(start != NULL);
    assert(size != 0);
    root_registrations++;
}

void collector_unregister_roots(void *start, size_t size)
{
    assert(start != NULL);
    assert(size != 0);
    root_unregistrations++;
}

void collector_pin_reference(uintptr_t *slot)
{
    assert(slot != NULL);
    pins++;
}

void collector_unpin_reference(uintptr_t *slot)
{
    assert(slot != NULL);
    unpins++;
}

int main(void)
{
    NetWasmStrongHandleTable table = {0};
    uint32_t null_handle = strong_handle_table_create(&table, 0);
    uint32_t value_handle = strong_handle_table_create(&table, 41);

    assert(null_handle == 1);
    assert(value_handle == 2);
    assert(strong_handle_table_get(&table, null_handle) == 0);
    assert(strong_handle_table_get(&table, value_handle) == 41);
    assert(root_registrations == 2);
    assert(root_unregistrations == 1);
    assert(strong_handle_table_count(&table) == 2);

    uint32_t pinned_handle = strong_handle_table_create_pinned(&table, 59);
    assert(strong_handle_table_get(&table, pinned_handle) == 59);
    assert(pins == 1);
    strong_handle_table_set(&table, pinned_handle, 61);
    assert(pins == 2);
    assert(unpins == 1);
    strong_handle_table_release(&table, pinned_handle);
    assert(unpins == 2);
    assert(strong_handle_table_count(&table) == 2);

    strong_handle_table_set(&table, value_handle, 73);
    assert(strong_handle_table_get(&table, value_handle) == 73);
    strong_handle_table_release(&table, null_handle);
    assert(strong_handle_table_create(&table, 89) == null_handle);

    strong_handle_table_release(&table, null_handle);
    strong_handle_table_set(&table, null_handle, 97);
    strong_handle_table_set(&table, 0, 97);
    strong_handle_table_set(&table, table.capacity + 1, 97);
    strong_handle_table_release(&table, 0);
    strong_handle_table_release(&table, table.capacity + 1);
    assert(strong_handle_table_get(&table, null_handle) == 0);
    assert(strong_handle_table_get(&table, 0) == 0);
    assert(strong_handle_table_get(&table, table.capacity + 1) == 0);
    assert(strong_handle_table_count(&table) == 1);
    return 0;
}
