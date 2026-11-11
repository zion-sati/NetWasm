/* SPDX-License-Identifier: MIT */
#include "collector_metadata.h"
#include "netwasm/address_index.h"

#include <assert.h>
#include <stdlib.h>

static bool fail_allocation;
static int allocations_before_failure = -1;

void *collector_allocate_metadata(size_t size)
{
    if (allocations_before_failure == 0) return NULL;
    if (allocations_before_failure > 0) allocations_before_failure--;
    return fail_allocation ? NULL : calloc(1, size);
}

void collector_release_metadata(void *address)
{
    free(address);
}

enum { KEYS = 4096 };

static uintptr_t keys[KEYS];
static bool present[KEYS];
static uint64_t random_state = 0x2545f4914f6cdd1d;

static uint32_t next_random(void)
{
    random_state ^= random_state << 13;
    random_state ^= random_state >> 7;
    random_state ^= random_state << 17;
    return (uint32_t)random_state;
}

static void *value_of(size_t index)
{
    return &present[index];
}

static void assert_matches_reference(const NetWasmAddressIndex *index)
{
    size_t count = 0;
    for (size_t key = 0; key < KEYS; key++) {
        void *found = netwasm_address_index_find(index, keys[key]);
        assert(found == (present[key] ? value_of(key) : NULL));
        count += present[key];
    }
    assert(index->count == count);
    assert(index->capacity == 0 || index->count * 2 <= index->capacity);
}

static void empty_index_finds_nothing(void)
{
    NetWasmAddressIndex index = {0};
    assert(netwasm_address_index_find(&index, 64) == NULL);
    assert(netwasm_address_index_find(&index, 0) == NULL);
    netwasm_address_index_remove(&index, 64);
    netwasm_address_index_remove(&index, 0);
    assert(index.count == 0 && index.capacity == 0);
}

/* Aligned addresses, as registries use, in a range dense enough that probe runs wrap and
 * removals shift entries back across the end of the table. */
static void churn_matches_a_reference(void)
{
    NetWasmAddressIndex index = {0};
    for (size_t key = 0; key < KEYS; key++) keys[key] = 0x10000 + key * 16;
    for (int step = 0; step < 200000; step++) {
        size_t key = next_random() % KEYS;
        if (present[key]) {
            netwasm_address_index_remove(&index, keys[key]);
            present[key] = false;
        } else {
            assert(netwasm_address_index_insert(&index, keys[key], value_of(key)));
            present[key] = true;
        }
        if (step % 997 == 0) assert_matches_reference(&index);
    }
    assert_matches_reference(&index);
    for (size_t key = 0; key < KEYS; key++) {
        if (!present[key]) continue;
        netwasm_address_index_remove(&index, keys[key]);
        present[key] = false;
    }
    assert_matches_reference(&index);
    assert(index.count == 0);
    netwasm_address_index_remove(&index, keys[0]);
    collector_release_metadata(index.keys);
    collector_release_metadata(index.values);
}

/* Without storage to grow into, an insert fails and leaves every entry where it was. */
static void failed_growth_leaves_the_index_unchanged(void)
{
    NetWasmAddressIndex index = {0};
    fail_allocation = true;
    assert(!netwasm_address_index_insert(&index, 64, value_of(0)));
    assert(index.count == 0 && index.capacity == 0);
    fail_allocation = false;
    size_t inserted = 0;
    while ((inserted + 1) * 2 <= 16) {
        assert(netwasm_address_index_insert(&index, 64 + inserted * 8, value_of(inserted)));
        inserted++;
    }
    assert(index.capacity == 16);
    fail_allocation = true;
    assert(!netwasm_address_index_insert(&index, 4096, value_of(0)));
    fail_allocation = false;
    allocations_before_failure = 1;
    assert(!netwasm_address_index_insert(&index, 4096, value_of(0)));
    allocations_before_failure = -1;
    assert(index.capacity == 16 && index.count == inserted);
    for (size_t key = 0; key < inserted; key++)
        assert(netwasm_address_index_find(&index, 64 + key * 8) == value_of(key));
    assert(netwasm_address_index_find(&index, 4096) == NULL);
    collector_release_metadata(index.keys);
    collector_release_metadata(index.values);
}

int main(void)
{
    empty_index_finds_nothing();
    churn_matches_a_reference();
    failed_growth_leaves_the_index_unchanged();
    return 0;
}
