#include "collector/collector_ephemeron.h"
#include "collector/collector_metadata.h"
#include "collector/ephemeron_handle_table.h"

#include <assert.h>
#include <stdbool.h>
#include <stdlib.h>

static uintptr_t *registered_key_slot;
static uintptr_t *registered_value_slot;
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

int collector_register_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    assert(key_slot != NULL);
    assert(value_slot != NULL);
    assert(*key_slot != 0);
    registered_key_slot = key_slot;
    registered_value_slot = value_slot;
    registrations++;
    return true;
}

void collector_unregister_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    assert(key_slot != NULL);
    assert(value_slot != NULL);
    unregistrations++;
    if (registered_key_slot == key_slot && registered_value_slot == value_slot) {
        registered_key_slot = NULL;
        registered_value_slot = NULL;
    }
}

int main(void)
{
    NetWasmEphemeronHandleTable table = {0};

    uint32_t first = ephemeron_handle_table_create(&table, 41, 73);
    assert(first == 1);
    assert(ephemeron_handle_table_get_key(&table, first) == 41);
    assert(ephemeron_handle_table_get_value(&table, first) == 73);
    assert(registered_key_slot == &table.keys[first - 1]);
    assert(registered_value_slot == &table.values[first - 1]);

    unsigned before = registrations;
    uint32_t second = ephemeron_handle_table_create(&table, 89, 0);
    assert(second == 2);
    assert(ephemeron_handle_table_get_key(&table, second) == 89);
    assert(ephemeron_handle_table_get_value(&table, second) == 0);
    assert(registrations > before);
    assert(unregistrations > 0);

    *registered_key_slot = 0;
    assert(ephemeron_handle_table_get_key(&table, second) == 0);

    ephemeron_handle_table_release(&table, first);
    uint32_t reused = ephemeron_handle_table_create(&table, 97, 101);
    assert(reused == first);
    uint32_t after_tombstone = ephemeron_handle_table_create(&table, 103, 107);
    assert(after_tombstone == 3);
    assert(ephemeron_handle_table_get_key(&table, second) == 0);
    assert(ephemeron_handle_table_get_value(&table, second) == 0);
    assert(ephemeron_handle_table_get_key(&table, after_tombstone) == 103);
    assert(ephemeron_handle_table_get_value(&table, after_tombstone) == 107);
    ephemeron_handle_table_release(&table, reused);
    ephemeron_handle_table_release(&table, after_tombstone);
    assert(ephemeron_handle_table_get_key(&table, reused) == 0);
    assert(ephemeron_handle_table_get_value(&table, reused) == 0);

    ephemeron_handle_table_release(&table, 0);
    ephemeron_handle_table_release(&table, table.capacity + 1);
    assert(ephemeron_handle_table_get_key(&table, 0) == 0);
    assert(ephemeron_handle_table_get_value(&table, table.capacity + 1) == 0);
    return 0;
}
