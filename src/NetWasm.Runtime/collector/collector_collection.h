// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_COLLECTOR_COLLECTION_H
#define NETWASM_COLLECTOR_COLLECTION_H

#include <stdbool.h>
#include <stdint.h>

void collector_collect(void);
/* GC.Collect(0): the youngest generation alone, which is every object without generations. */
void collector_collect_young(void);
/* The embedder is idle: collects when the collector finds it worth its while, and says whether it did. */
bool collector_collect_when_idle(void);
/* The embedder has stayed quiet since the program last ran: collects whatever the heap has grown by. Zero when it
 * did not collect, otherwise 1 plus the share of the heap it freed, in 255ths. */
unsigned collector_collect_when_quiet(void);
uint64_t collector_collection_count(void);
/* The collections that collected the oldest generation: every collection, without generations. */
uint64_t collector_oldest_generation_collection_count(void);
/* The generation of an object: the oldest once it has survived a collection, 0 without generations. */
uint32_t collector_object_generation(uintptr_t object);
uint64_t collector_total_allocated_bytes(void);
uint64_t collector_heap_size(void);
uint64_t collector_free_bytes(void);
bool collector_try_read_total_pause_milliseconds(uint64_t *value);

#endif
