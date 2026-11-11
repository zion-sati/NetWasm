/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "address_index.h"

#include "../collector_metadata.h"

#include <stdlib.h>

enum { MINIMUM_CAPACITY = 16 };

/* Fibonacci hashing spreads aligned addresses, whose low bits are always zero. */
static size_t home_of(uintptr_t key, size_t capacity)
{
    uint64_t mixed = (uint64_t)key * UINT64_C(0x9e3779b97f4a7c15);
    return (size_t)(mixed ^ (mixed >> 32)) & (capacity - 1);
}

static size_t slot_of(const NetWasmAddressIndex *index, uintptr_t key)
{
    size_t slot = home_of(key, index->capacity);
    while (index->keys[slot] != 0 && index->keys[slot] != key)
        slot = (slot + 1) & (index->capacity - 1);
    return slot;
}

void *netwasm_address_index_find(const NetWasmAddressIndex *index, uintptr_t key)
{
    if (key == 0 || index->capacity == 0) return NULL;
    size_t slot = slot_of(index, key);
    return index->keys[slot] == key ? index->values[slot] : NULL;
}

static bool grow(NetWasmAddressIndex *index)
{
    size_t capacity = index->capacity == 0 ? MINIMUM_CAPACITY : index->capacity * 2;
    uintptr_t *keys = collector_allocate_metadata(capacity * sizeof(*keys));
    void **values = collector_allocate_metadata(capacity * sizeof(*values));
    if (keys == NULL || values == NULL) {
        collector_release_metadata(keys);
        collector_release_metadata(values);
        return false;
    }
    NetWasmAddressIndex grown = {keys, values, capacity, index->count};
    for (size_t old = 0; old < index->capacity; old++) {
        if (index->keys[old] == 0) continue;
        size_t slot = slot_of(&grown, index->keys[old]);
        keys[slot] = index->keys[old];
        values[slot] = index->values[old];
    }
    collector_release_metadata(index->keys);
    collector_release_metadata(index->values);
    *index = grown;
    return true;
}

bool netwasm_address_index_insert(NetWasmAddressIndex *index, uintptr_t key, void *value)
{
    if (key == 0 || value == NULL) abort();
    /* At most half full, so probe runs stay short. */
    if ((index->count + 1) * 2 > index->capacity && !grow(index)) return false;
    size_t slot = slot_of(index, key);
    if (index->keys[slot] == key) abort();
    index->keys[slot] = key;
    index->values[slot] = value;
    index->count++;
    return true;
}

void netwasm_address_index_remove(NetWasmAddressIndex *index, uintptr_t key)
{
    if (key == 0 || index->capacity == 0) return;
    size_t mask = index->capacity - 1;
    size_t hole = slot_of(index, key);
    if (index->keys[hole] != key) return;
    /* Shift later entries of the probe run back, so no tombstone lengthens later probes. */
    for (size_t next = (hole + 1) & mask; index->keys[next] != 0; next = (next + 1) & mask) {
        size_t home = home_of(index->keys[next], index->capacity);
        bool stays = hole < next ? hole < home && home <= next : hole < home || home <= next;
        if (stays) continue;
        index->keys[hole] = index->keys[next];
        index->values[hole] = index->values[next];
        hole = next;
    }
    index->keys[hole] = 0;
    index->values[hole] = NULL;
    index->count--;
}
