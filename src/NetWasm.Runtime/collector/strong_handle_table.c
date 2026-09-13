#include "strong_handle_table.h"

#include "collector_metadata.h"
#include "collector_pinning.h"
#include "collector_roots.h"

#include <stdlib.h>
#include <string.h>

static void grow(NetWasmStrongHandleTable *table)
{
    uint32_t capacity = table->capacity == 0 ? 1 : table->capacity * 2;
    if (capacity <= table->capacity) abort();

    uintptr_t *slots = collector_allocate_metadata(capacity * sizeof(uintptr_t));
    uint8_t *occupied = collector_allocate_metadata(capacity * sizeof(uint8_t));
    uint8_t *pinned = collector_allocate_metadata(capacity * sizeof(uint8_t));
    if (slots == NULL || occupied == NULL || pinned == NULL) abort();

    memset(slots, 0, capacity * sizeof(uintptr_t));
    memset(occupied, 0, capacity * sizeof(uint8_t));
    memset(pinned, 0, capacity * sizeof(uint8_t));
    if (table->slots != NULL) {
        memcpy(slots, table->slots, table->capacity * sizeof(uintptr_t));
        memcpy(occupied, table->occupied, table->capacity * sizeof(uint8_t));
        memcpy(pinned, table->pinned, table->capacity * sizeof(uint8_t));
        for (uint32_t index = 0; index < table->capacity; index++) {
            if (table->pinned[index] != 0) collector_unpin_reference(&table->slots[index]);
        }
        collector_unregister_roots(table->slots, table->capacity * sizeof(uintptr_t));
    }

    collector_register_roots(slots, capacity * sizeof(uintptr_t));
    collector_release_metadata(table->slots);
    collector_release_metadata(table->occupied);
    collector_release_metadata(table->pinned);
    table->slots = slots;
    table->occupied = occupied;
    table->pinned = pinned;
    table->capacity = capacity;

    for (uint32_t index = 0; index < table->capacity; index++) {
        if (table->pinned[index] != 0) collector_pin_reference(&table->slots[index]);
    }
}

uint32_t strong_handle_table_create(NetWasmStrongHandleTable *table, uintptr_t target)
{
    uint32_t index = 0;
    while (index < table->capacity && table->occupied[index] != 0) index++;
    if (index == table->capacity) grow(table);

    table->occupied[index] = 1;
    table->slots[index] = target;
    return index + 1;
}

uint32_t strong_handle_table_create_pinned(NetWasmStrongHandleTable *table, uintptr_t target)
{
    uint32_t handle = strong_handle_table_create(table, target);
    uint32_t index = handle - 1;
    table->pinned[index] = 1;
    collector_pin_reference(&table->slots[index]);
    return handle;
}

uintptr_t strong_handle_table_get(const NetWasmStrongHandleTable *table, uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return 0;
    return table->slots[handle - 1];
}

void strong_handle_table_set(NetWasmStrongHandleTable *table, uint32_t handle, uintptr_t target)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return;
    uint32_t index = handle - 1;
    if (table->pinned[index] != 0) collector_unpin_reference(&table->slots[index]);
    table->slots[index] = target;
    if (table->pinned[index] != 0) collector_pin_reference(&table->slots[index]);
}

void strong_handle_table_release(NetWasmStrongHandleTable *table, uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return;
    uint32_t index = handle - 1;
    if (table->pinned[index] != 0) collector_unpin_reference(&table->slots[index]);
    table->slots[index] = 0;
    table->occupied[index] = 0;
    table->pinned[index] = 0;
}

uint32_t strong_handle_table_count(const NetWasmStrongHandleTable *table)
{
    uint32_t count = 0;
    for (uint32_t index = 0; index < table->capacity; index++) {
        count += table->occupied[index] != 0;
    }
    return count;
}
