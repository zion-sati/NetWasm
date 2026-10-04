#include "ephemeron_handle_table.h"

#include "collector_ephemeron.h"
#include "collector_metadata.h"

#include <stdlib.h>
#include <string.h>

static void register_entry(NetWasmEphemeronHandleTable *table, uint32_t index)
{
    if (!collector_register_ephemeron(&table->keys[index], &table->values[index])) {
        abort();
    }
}

static void grow(NetWasmEphemeronHandleTable *table)
{
    uint32_t capacity = table->capacity == 0 ? 1 : table->capacity * 2;
    if (capacity <= table->capacity) abort();

    uintptr_t *keys = collector_allocate_metadata(capacity * sizeof(uintptr_t));
    uintptr_t *values = collector_allocate_metadata(capacity * sizeof(uintptr_t));
    uint8_t *occupied = collector_allocate_metadata(capacity * sizeof(uint8_t));
    if (keys == NULL || values == NULL || occupied == NULL) abort();

    memset(keys, 0, capacity * sizeof(uintptr_t));
    memset(values, 0, capacity * sizeof(uintptr_t));
    memset(occupied, 0, capacity * sizeof(uint8_t));

    for (uint32_t index = 0; index < table->capacity; index++) {
        if (table->occupied[index] != 0) {
            collector_unregister_ephemeron(&table->keys[index], &table->values[index]);
            occupied[index] = 1;
            if (table->keys[index] != 0) {
                keys[index] = table->keys[index];
                values[index] = table->values[index];
            }
        }
    }

    collector_release_metadata(table->keys);
    collector_release_metadata(table->values);
    collector_release_metadata(table->occupied);
    table->keys = keys;
    table->values = values;
    table->occupied = occupied;
    table->capacity = capacity;

    for (uint32_t index = 0; index < table->capacity; index++) {
        if (table->occupied[index] != 0 && table->keys[index] != 0) {
            register_entry(table, index);
        }
    }
}

uint32_t ephemeron_handle_table_create(
    NetWasmEphemeronHandleTable *table,
    uintptr_t key,
    uintptr_t value)
{
    if (key == 0) abort();

    uint32_t index = 0;
    while (index < table->capacity && table->occupied[index] != 0) index++;
    if (index == table->capacity) grow(table);

    table->occupied[index] = 1;
    table->keys[index] = key;
    table->values[index] = value;
    register_entry(table, index);
    return index + 1;
}

uintptr_t ephemeron_handle_table_get_key(
    const NetWasmEphemeronHandleTable *table,
    uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0 ||
        table->keys[handle - 1] == 0) {
        return 0;
    }
    return table->keys[handle - 1];
}

uintptr_t ephemeron_handle_table_get_value(
    const NetWasmEphemeronHandleTable *table,
    uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0 ||
        table->keys[handle - 1] == 0) {
        return 0;
    }
    return table->values[handle - 1];
}

void ephemeron_handle_table_release(
    NetWasmEphemeronHandleTable *table,
    uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return;

    uint32_t index = handle - 1;
    collector_unregister_ephemeron(&table->keys[index], &table->values[index]);
    table->keys[index] = 0;
    table->values[index] = 0;
    table->occupied[index] = 0;
}
