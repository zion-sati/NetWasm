#include "weak_handle_table.h"

#include "collector_metadata.h"
#include "collector_weak_reference.h"

#include <stdlib.h>
#include <string.h>

static void register_target(NetWasmWeakHandleTable *table, uint32_t index)
{
    if (table->slots[index] == 0) return;

    if (!collector_register_weak_reference(
            &table->slots[index],
            table->slots[index],
            (NetWasmWeakReferenceLifetime)table->lifetimes[index])) {
        abort();
    }
}

static void grow(NetWasmWeakHandleTable *table)
{
    uint32_t capacity = table->capacity == 0 ? 1 : table->capacity * 2;
    if (capacity <= table->capacity) abort();

    uintptr_t *slots = collector_allocate_metadata(capacity * sizeof(uintptr_t));
    uint8_t *lifetimes = collector_allocate_metadata(capacity * sizeof(uint8_t));
    uint8_t *occupied = collector_allocate_metadata(capacity * sizeof(uint8_t));
    if (slots == NULL || lifetimes == NULL || occupied == NULL) abort();

    memset(slots, 0, capacity * sizeof(uintptr_t));
    memset(lifetimes, 0, capacity * sizeof(uint8_t));
    memset(occupied, 0, capacity * sizeof(uint8_t));

    for (uint32_t index = 0; index < table->capacity; index++) {
        if (table->slots[index] != 0) {
            collector_unregister_weak_reference(
                &table->slots[index],
                (NetWasmWeakReferenceLifetime)table->lifetimes[index]);
        }

        slots[index] = table->slots[index];
        lifetimes[index] = table->lifetimes[index];
        occupied[index] = table->occupied[index];
    }

    collector_release_metadata(table->slots);
    collector_release_metadata(table->lifetimes);
    collector_release_metadata(table->occupied);
    table->slots = slots;
    table->lifetimes = lifetimes;
    table->occupied = occupied;
    table->capacity = capacity;

    for (uint32_t index = 0; index < table->capacity; index++) {
        if (table->occupied[index] != 0) register_target(table, index);
    }
}

uint32_t weak_handle_table_create(
    NetWasmWeakHandleTable *table,
    uintptr_t target,
    bool track_resurrection)
{
    uint32_t index = 0;
    while (index < table->capacity && table->occupied[index] != 0) index++;
    if (index == table->capacity) grow(table);

    table->occupied[index] = 1;
    table->slots[index] = target;
    table->lifetimes[index] = track_resurrection
        ? NETWASM_WEAK_REFERENCE_AFTER_FINALIZATION
        : NETWASM_WEAK_REFERENCE_BEFORE_FINALIZATION;
    register_target(table, index);
    return index + 1;
}

uintptr_t weak_handle_table_get(const NetWasmWeakHandleTable *table, uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return 0;
    return table->slots[handle - 1];
}

void weak_handle_table_set(NetWasmWeakHandleTable *table, uint32_t handle, uintptr_t target)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return;

    uint32_t index = handle - 1;
    if (table->slots[index] != 0) {
        collector_unregister_weak_reference(
            &table->slots[index],
            (NetWasmWeakReferenceLifetime)table->lifetimes[index]);
    }

    table->slots[index] = target;
    register_target(table, index);
}

void weak_handle_table_release(NetWasmWeakHandleTable *table, uint32_t handle)
{
    if (handle == 0 || handle > table->capacity || table->occupied[handle - 1] == 0) return;

    uint32_t index = handle - 1;
    if (table->slots[index] != 0) {
        collector_unregister_weak_reference(
            &table->slots[index],
            (NetWasmWeakReferenceLifetime)table->lifetimes[index]);
    }

    table->slots[index] = 0;
    table->lifetimes[index] = 0;
    table->occupied[index] = 0;
}
