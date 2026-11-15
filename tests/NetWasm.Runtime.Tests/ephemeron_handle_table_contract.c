// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include "collector/collector_ephemeron.h"
#include "collector/collector_metadata.h"
#include "collector/ephemeron_handle_table.h"

#include <assert.h>
#include <stdbool.h>
#include <stdlib.h>

#define MAX_REGISTRATIONS 16

static uintptr_t *registered_key_slots[MAX_REGISTRATIONS];
static uintptr_t *registered_value_slots[MAX_REGISTRATIONS];
static unsigned registrations;
static unsigned unregistrations;

static bool is_registered(uintptr_t *key_slot, uintptr_t *value_slot)
{
    for (unsigned index = 0; index < MAX_REGISTRATIONS; index++) {
        if (registered_key_slots[index] == key_slot &&
            registered_value_slots[index] == value_slot) {
            return true;
        }
    }
    return false;
}

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
    for (unsigned index = 0; index < MAX_REGISTRATIONS; index++) {
        if (registered_key_slots[index] == NULL) {
            registered_key_slots[index] = key_slot;
            registered_value_slots[index] = value_slot;
            registrations++;
            return true;
        }
    }
    abort();
}

void collector_unregister_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot)
{
    assert(key_slot != NULL);
    assert(value_slot != NULL);
    for (unsigned index = 0; index < MAX_REGISTRATIONS; index++) {
        if (registered_key_slots[index] == key_slot &&
            registered_value_slots[index] == value_slot) {
            registered_key_slots[index] = NULL;
            registered_value_slots[index] = NULL;
            unregistrations++;
            return;
        }
    }
}

int main(void)
{
    NetWasmEphemeronHandleTable table = {0};

    uint32_t first = ephemeron_handle_table_create(&table, 41, 73);
    assert(first == 1);
    assert(ephemeron_handle_table_get_key(&table, first) == 41);
    assert(ephemeron_handle_table_get_value(&table, first) == 73);
    assert(is_registered(&table.keys[first - 1], &table.values[first - 1]));

    unsigned before = registrations;
    uint32_t second = ephemeron_handle_table_create(&table, 89, 0);
    assert(second == 2);
    assert(ephemeron_handle_table_get_key(&table, second) == 89);
    assert(ephemeron_handle_table_get_value(&table, second) == 0);
    assert(registrations > before);
    assert(unregistrations > 0);

    ephemeron_handle_table_set_value(&table, second, 113);
    assert(ephemeron_handle_table_get_value(&table, second) == 113);

    table.keys[second - 1] = 0;
    assert(is_registered(&table.keys[second - 1], &table.values[second - 1]));
    ephemeron_handle_table_set_target_to_null(&table, second);
    assert(!is_registered(&table.keys[second - 1], &table.values[second - 1]));
    assert(ephemeron_handle_table_get_key(&table, second) == 0);
    assert(ephemeron_handle_table_get_value(&table, second) == 0);
    ephemeron_handle_table_set_value(&table, second, 127);
    assert(ephemeron_handle_table_get_value(&table, second) == 0);

    uint32_t empty = ephemeron_handle_table_create(&table, 0, 131);
    assert(empty == 3);
    assert(ephemeron_handle_table_get_key(&table, empty) == 0);
    assert(ephemeron_handle_table_get_value(&table, empty) == 0);

    ephemeron_handle_table_release(&table, first);
    uint32_t reused = ephemeron_handle_table_create(&table, 97, 101);
    assert(reused == first);
    uint32_t after_tombstone = ephemeron_handle_table_create(&table, 103, 107);
    assert(after_tombstone == 4);
    assert(ephemeron_handle_table_get_key(&table, second) == 0);
    assert(ephemeron_handle_table_get_value(&table, second) == 0);
    assert(ephemeron_handle_table_get_key(&table, after_tombstone) == 103);
    assert(ephemeron_handle_table_get_value(&table, after_tombstone) == 107);
    uint32_t after_empty_grow = ephemeron_handle_table_create(&table, 137, 139);
    assert(after_empty_grow == 5);
    assert(ephemeron_handle_table_get_key(&table, after_empty_grow) == 137);
    assert(ephemeron_handle_table_get_value(&table, after_empty_grow) == 139);

    uint32_t collected_before_grow = ephemeron_handle_table_create(&table, 149, 151);
    uintptr_t *old_collected_key_slot = &table.keys[collected_before_grow - 1];
    uintptr_t *old_collected_value_slot = &table.values[collected_before_grow - 1];
    table.keys[collected_before_grow - 1] = 0;
    assert(is_registered(old_collected_key_slot, old_collected_value_slot));
    uint32_t fill_seven = ephemeron_handle_table_create(&table, 157, 163);
    uint32_t fill_eight = ephemeron_handle_table_create(&table, 167, 173);
    uint32_t trigger_dead_key_grow = ephemeron_handle_table_create(&table, 179, 181);
    assert(trigger_dead_key_grow == 9);
    assert(!is_registered(old_collected_key_slot, old_collected_value_slot));
    assert(!is_registered(
        &table.keys[collected_before_grow - 1],
        &table.values[collected_before_grow - 1]));

    uint32_t collected_before_release = ephemeron_handle_table_create(&table, 191, 193);
    uintptr_t *release_key_slot = &table.keys[collected_before_release - 1];
    uintptr_t *release_value_slot = &table.values[collected_before_release - 1];
    table.keys[collected_before_release - 1] = 0;
    assert(is_registered(release_key_slot, release_value_slot));
    ephemeron_handle_table_release(&table, collected_before_release);
    assert(!is_registered(release_key_slot, release_value_slot));

    ephemeron_handle_table_release(&table, reused);
    ephemeron_handle_table_release(&table, after_tombstone);
    ephemeron_handle_table_release(&table, empty);
    ephemeron_handle_table_release(&table, after_empty_grow);
    ephemeron_handle_table_release(&table, collected_before_grow);
    ephemeron_handle_table_release(&table, fill_seven);
    ephemeron_handle_table_release(&table, fill_eight);
    ephemeron_handle_table_release(&table, trigger_dead_key_grow);
    assert(ephemeron_handle_table_get_key(&table, reused) == 0);
    assert(ephemeron_handle_table_get_value(&table, reused) == 0);

    ephemeron_handle_table_release(&table, 0);
    ephemeron_handle_table_release(&table, table.capacity + 1);
    assert(ephemeron_handle_table_get_key(&table, 0) == 0);
    assert(ephemeron_handle_table_get_value(&table, table.capacity + 1) == 0);
    return 0;
}
