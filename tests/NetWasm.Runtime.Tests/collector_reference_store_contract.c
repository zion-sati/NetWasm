// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "collector/collector_reference_store.h"

#ifdef NETWASM_GC_GENERATIONAL
/* The collector's card directory and sink, which belong to NetWasmGC: with no leaf every card is in the sink. */
uintptr_t netwasm_gc_card_directory[NWGC_CARD_DIRECTORY_ENTRIES];
unsigned char netwasm_gc_card_sink[NWGC_CARD_LEAF_CARDS];

static size_t set_cards(void)
{
    size_t set = 0;
    for (size_t card = 0; card < NWGC_CARD_LEAF_CARDS; card++) set += netwasm_gc_card_sink[card];
    return set;
}

/* With generations a stored reference sets the card of its slot, a moved range sets every card it overlaps, and
 * clearing, which stores no reference, sets none. */
static void stores_are_recorded(void)
{
    static netwasm_reference_t slots[1024];
    collector_store_reference(17, &slots[0], 41);
    if (*netwasm_gc_card((uintptr_t)&slots[0]) != 1 || set_cards() != 1) abort();
    collector_clear_value_range(17, &slots[512], 256, sizeof(slots[0]));
    if (set_cards() != 1) abort();
    collector_move_value_range(17, &slots[256], &slots[0], 256, sizeof(slots[0]));
    uintptr_t first = (uintptr_t)&slots[256];
    uintptr_t last = (uintptr_t)&slots[512] - 1;
    for (uintptr_t address = first; address <= last; address += NWGC_CARD_BYTES)
        if (*netwasm_gc_card(address) != 1) abort();
    if (*netwasm_gc_card(last) != 1) abort();
    size_t moved = (size_t)((last >> NWGC_CARD_SHIFT) - (first >> NWGC_CARD_SHIFT)) + 1;
    size_t shared = (first >> NWGC_CARD_SHIFT) == ((uintptr_t)&slots[0] >> NWGC_CARD_SHIFT) ? 1 : 0;
    if (set_cards() != 1 + moved - shared) abort();
    memset(netwasm_gc_card_sink, 0, sizeof(netwasm_gc_card_sink));
}
#endif

int main(void)
{
#ifdef NETWASM_GC_GENERATIONAL
    stores_are_recorded();
#endif
    netwasm_reference_t slot = 0;
    collector_store_reference(17, &slot, 41);
    if (slot != 41) {
        abort();
    }
    collector_store_reference(17, &slot, 0);
    if (slot != 0) {
        abort();
    }
    uint32_t values[] = {1, 2, 3, 4};
    collector_move_value_range(
        17,
        &values[1],
        &values[0],
        3,
        sizeof(values[0]));
    const uint32_t moved[] = {1, 1, 2, 3};
    if (memcmp(values, moved, sizeof(values)) != 0) {
        abort();
    }
    collector_clear_value_range(
        17,
        &values[1],
        2,
        sizeof(values[0]));
    const uint32_t cleared[] = {1, 0, 0, 3};
    if (memcmp(values, cleared, sizeof(values)) != 0) {
        abort();
    }
    puts("Collector reference-store contract PASS");
    return 0;
}
