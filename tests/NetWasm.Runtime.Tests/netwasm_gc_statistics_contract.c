// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>
#include <stdlib.h>

#define BLOCK_BYTES (64u * 1024u)
#define OBJECTS 3000u

static uintptr_t roots[OBJECTS + 1];
static size_t root_count;
static const size_t reference_offsets[] = { 0 };
static const NetWasmGcDescriptor referencing = { reference_offsets, 1 };

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    return malloc(bytes);
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    free(address);
}

static void trace_roots(NetWasmGc *gc, void *state)
{
    (void)state;
    for (size_t index = 0; index < root_count; index++) netwasm_gc_mark(gc, roots[index]);
}

static void *allocate(NetWasmGc *gc, size_t count, size_t size, const NetWasmGcDescriptor *descriptor)
{
    void *result = test_try_allocate_available(gc, count, size, descriptor);
    if (result == NULL) assert(test_allocate(gc, count, size, descriptor, &result) == NETWASM_GC_OK);
    return result;
}

static NetWasmGcStatistics read_partitioned(const NetWasmGc *gc)
{
    NetWasmGcStatistics statistics;
    netwasm_gc_read_statistics(gc, &statistics);
    assert(statistics.arena_bytes ==
        (statistics.block_count + statistics.empty_block_count) * BLOCK_BYTES + statistics.unused_arena_bytes);
    assert(statistics.block_count * BLOCK_BYTES ==
        statistics.block_metadata_bytes + statistics.block_tail_bytes + statistics.small_occupied_bytes +
        statistics.reserved_run_bytes + statistics.free_slot_bytes);
    assert(statistics.object_count == netwasm_gc_object_count(gc));
    /* Live bytes count whole small slots: payload plus padding. */
    assert(statistics.live_payload_bytes + statistics.small_padding_bytes == netwasm_gc_live_bytes(gc));
    assert(statistics.small_header_bytes == 0);
    assert(statistics.collections == netwasm_gc_collection_count(gc));
    assert(statistics.directory_bytes > 0);
    return statistics;
}

int main(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, NULL);

    /* Two size classes, every other object rooted, plus one rooted and one
     * unreachable large object. */
    for (size_t index = 0; index < OBJECTS; index++) {
        void *object = index % 3 == 0 ? allocate(gc, 1, 200, NULL) : allocate(gc, 1, 24, &referencing);
        if (index % 2 == 0) roots[root_count++] = (uintptr_t)object;
    }
    roots[root_count++] = (uintptr_t)allocate(gc, 1024, 8, NULL);
    (void)allocate(gc, 1024, 8, NULL);

    NetWasmGcStatistics allocated = read_partitioned(gc);
    assert(allocated.fast_allocations + allocated.small_allocations == OBJECTS);
    assert(allocated.large_allocations == 2);
    assert(allocated.large_object_count == 2);
    assert(allocated.object_count == OBJECTS + 2);
    assert(allocated.run_slots >= OBJECTS);
    assert(allocated.run_bytes_zeroed >= allocated.small_occupied_bytes);
    assert(allocated.fresh_blocks == allocated.block_count);
    assert(allocated.small_padding_bytes < allocated.small_occupied_bytes);
    assert(allocated.collections == 0);

    netwasm_gc_collect(gc);
    NetWasmGcStatistics collected = read_partitioned(gc);
    assert(collected.collections == 1);
    assert(collected.root_marks == root_count);
    assert(collected.marked_objects == root_count);
    assert(collected.object_count == root_count);
    assert(collected.freed_large_objects == 1);
    assert(collected.large_object_count == 1);
    assert(collected.reserved_run_bytes == 0);
    assert(collected.swept_blocks == allocated.block_count);

    /* Half of the 2,000 24-byte objects survive; refilling fewer than the
     * 1,000 holes left between them takes no new block. */
    size_t fresh_before = collected.fresh_blocks;
    for (size_t index = 0; index < OBJECTS / 4; index++) (void)allocate(gc, 1, 24, &referencing);
    NetWasmGcStatistics refilled = read_partitioned(gc);
    assert(refilled.free_run_searches > collected.free_run_searches);
    assert(refilled.bitmap_words_searched > collected.bitmap_words_searched);
    assert(refilled.fresh_blocks == fresh_before);

    netwasm_gc_destroy(gc);
    return 0;
}
