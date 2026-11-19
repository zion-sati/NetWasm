// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

/* When NetWasmGC collects: at twice what survived plus the minimum budget, at three times when most of the
 * heap survived, never before small objects refill the most they have occupied, at once when no backing
 * memory is left, and for an idle embedder once the heap has grown enough. */
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>

#define MIB ((size_t)1024 * 1024)
/* A small object that fills one 3,008-byte slot, so every object occupies exactly SLOT bytes. */
#define OBJECT_BYTES 3000u
#define SLOT 3008u

typedef union {
    struct {
        size_t bytes;
    } fields;
    max_align_t alignment;
} AllocationHeader;

typedef struct {
    uintptr_t *values;
    size_t count;
} Roots;

static size_t outstanding_bytes;
/* Backing memory the embedder has: an allocation that would pass it fails. */
static size_t memory_limit = SIZE_MAX;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    if (bytes > memory_limit || outstanding_bytes > memory_limit - bytes) return NULL;
    AllocationHeader *header = malloc(sizeof(*header) + bytes);
    assert(header != NULL);
    header->fields.bytes = bytes;
    outstanding_bytes += bytes;
    return header + 1;
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)alignment;
    AllocationHeader *header = (AllocationHeader *)address - 1;
    assert(header->fields.bytes == bytes);
    outstanding_bytes -= bytes;
    free(header);
}

static void trace_roots(NetWasmGc *gc, void *state)
{
    Roots *roots = state;
    for (size_t index = 0; index < roots->count; index++)
        netwasm_gc_mark(gc, roots->values[index]);
}

static NetWasmGc *create(size_t budget, Roots *roots)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, budget, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, roots);
    return gc;
}

/* Allocates small objects until the heap has occupied bytes more, rooting keep of every period. */
static void fill(NetWasmGc *gc, Roots *roots, size_t bytes, size_t keep, size_t period)
{
    for (size_t index = 0; index < bytes / SLOT; index++) {
        void *object = NULL;
        assert(test_allocate(gc, OBJECT_BYTES, 1, NULL, &object) == NETWASM_GC_OK);
        if (index % period < keep) roots->values[roots->count++] = (uintptr_t)object;
    }
}

/* A heap of garbage is collected each time it fills the minimum budget. */
static void garbage_collects_at_the_minimum_budget(void)
{
    uintptr_t values[1];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(MIB, &roots);
    fill(gc, &roots, 10 * MIB, 0, 1);
    assert(netwasm_gc_collection_count(gc) >= 9 && netwasm_gc_collection_count(gc) <= 10);
    netwasm_gc_destroy(gc);
}

/* Half the heap surviving is steady work: the next collection is due at twice the survivors plus the budget. */
static void steady_heap_grows_to_twice_the_survivors(void)
{
    static uintptr_t values[4096];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(MIB, &roots);
    /* The first collection, at 1 MiB, leaves half a MiB: the next is due at 2 MiB. */
    fill(gc, &roots, MIB + 4 * SLOT, 1, 2);
    assert(netwasm_gc_collection_count(gc) == 1);
    fill(gc, &roots, MIB + MIB / 4, 0, 1);
    assert(netwasm_gc_collection_count(gc) == 1);
    fill(gc, &roots, MIB / 2, 0, 1);
    assert(netwasm_gc_collection_count(gc) == 2);
    netwasm_gc_destroy(gc);
}

/* Three fifths surviving is a heap being built: the next collection waits for three times the survivors. */
static void growing_heap_grows_to_three_times_the_survivors(void)
{
    static uintptr_t values[8192];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(MIB, &roots);
    /* The first collection, at 1 MiB, leaves 0.6 MiB: the next is due at 2.8 MiB, not at 2.2 MiB. */
    fill(gc, &roots, MIB + 4 * SLOT, 3, 5);
    assert(netwasm_gc_collection_count(gc) == 1);
    fill(gc, &roots, MIB + 3 * MIB / 4, 0, 1);
    assert(netwasm_gc_collection_count(gc) == 1);
    fill(gc, &roots, MIB / 2, 0, 1);
    assert(netwasm_gc_collection_count(gc) == 2);
    netwasm_gc_destroy(gc);
}

/* Small objects' blocks stay with the collector, so a heap that has held 6 MiB of them is not collected again
 * before it holds 6 MiB, however little survived. */
static void small_objects_refill_their_peak(void)
{
    static uintptr_t values[4096];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(MIB, &roots);
    fill(gc, &roots, 6 * MIB, 1, 1);
    roots.count = 0;
    netwasm_gc_collect(gc);
    uint64_t collections = netwasm_gc_collection_count(gc);
    fill(gc, &roots, 5 * MIB + MIB / 2, 0, 1);
    assert(netwasm_gc_collection_count(gc) == collections);
    fill(gc, &roots, MIB, 0, 1);
    assert(netwasm_gc_collection_count(gc) == collections + 1);
    netwasm_gc_destroy(gc);
}

/* A dead large object's memory goes back to the embedder, so it does not raise that peak: after an 8 MiB
 * array dies, garbage is collected at the minimum budget again. */
static void large_objects_do_not_raise_the_peak(void)
{
    uintptr_t values[1];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(MIB, &roots);
    void *large = NULL;
    assert(test_allocate(gc, 8 * MIB, 1, NULL, &large) == NETWASM_GC_OK);
    netwasm_gc_collect(gc);
    uint64_t collections = netwasm_gc_collection_count(gc);
    fill(gc, &roots, MIB + MIB / 2, 0, 1);
    assert(netwasm_gc_collection_count(gc) == collections + 1);
    netwasm_gc_destroy(gc);
}

/* With no backing memory left before a collection is due, an allocation collects once and tries again; it
 * fails only when the collection freed nothing it can use. */
static void no_memory_for_a_block_collects_and_retries(void)
{
    static uintptr_t values[4096];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(SIZE_MAX, &roots);
    size_t before = outstanding_bytes;
    memory_limit = before + 4 * MIB;
    /* Garbage: the arenas stop at 3 MiB (1 MiB, then 2 MiB), and each collection empties their blocks. */
    fill(gc, &roots, 8 * MIB, 0, 1);
    assert(netwasm_gc_collection_count(gc) >= 2 && netwasm_gc_collection_count(gc) <= 3);
    /* Everything kept: the collection frees nothing, and the allocation reports that memory ran out. */
    uint64_t collections = netwasm_gc_collection_count(gc);
    NetWasmGcResult result = NETWASM_GC_OK;
    while (result == NETWASM_GC_OK) {
        void *object = NULL;
        result = test_allocate(gc, OBJECT_BYTES, 1, NULL, &object);
        if (result == NETWASM_GC_OK) roots.values[roots.count++] = (uintptr_t)object;
    }
    assert(result == NETWASM_GC_OUT_OF_MEMORY);
    assert(netwasm_gc_collection_count(gc) == collections + 2);
    memory_limit = SIZE_MAX;
    netwasm_gc_destroy(gc);
}

static void no_memory_for_a_large_object_collects_and_retries(void)
{
    uintptr_t values[4];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(SIZE_MAX, &roots);
    memory_limit = outstanding_bytes + 4 * MIB;
    /* Dead 1.5 MiB arrays: the third does not fit until a collection releases the first two. */
    for (size_t index = 0; index < 5; index++) {
        void *large = NULL;
        assert(test_allocate(gc, MIB + MIB / 2, 1, NULL, &large) == NETWASM_GC_OK);
    }
    assert(netwasm_gc_collection_count(gc) == 2);
    /* Kept arrays: the collection releases nothing, and the allocation reports that memory ran out. */
    NetWasmGcResult result = NETWASM_GC_OK;
    while (result == NETWASM_GC_OK) {
        void *large = NULL;
        result = test_allocate(gc, MIB + MIB / 2, 1, NULL, &large);
        if (result == NETWASM_GC_OK) roots.values[roots.count++] = (uintptr_t)large;
    }
    assert(result == NETWASM_GC_OUT_OF_MEMORY);
    assert(roots.count == 2);
    memory_limit = SIZE_MAX;
    netwasm_gc_destroy(gc);
}

/* An idle embedder's collection is worth its while once the heap has grown by a quarter of the minimum
 * budget and by half of what survived the last collection. */
static void idle_collects_once_the_heap_has_grown_enough(void)
{
    static uintptr_t values[4096];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(4 * MIB, &roots);
    assert(!netwasm_gc_collect_when_idle(gc));
    fill(gc, &roots, MIB / 2, 0, 1);
    assert(!netwasm_gc_collect_when_idle(gc));
    fill(gc, &roots, MIB, 0, 1);
    assert(netwasm_gc_collect_when_idle(gc));
    assert(netwasm_gc_collection_count(gc) == 1);
    assert(!netwasm_gc_collect_when_idle(gc));
    /* 3 MiB survive: growth of 1.2 MiB is not half of that, and growth of 1.7 MiB is. */
    fill(gc, &roots, 3 * MIB, 1, 1);
    netwasm_gc_collect(gc);
    fill(gc, &roots, MIB + MIB / 5, 0, 1);
    assert(!netwasm_gc_collect_when_idle(gc));
    fill(gc, &roots, MIB / 2, 0, 1);
    assert(netwasm_gc_collect_when_idle(gc));
    assert(netwasm_gc_collection_count(gc) == 3);
    assert(!netwasm_gc_collect_when_idle(NULL));
    netwasm_gc_destroy(gc);
}

/* A quiet embedder collects whatever the growth, and learns how much of the heap that freed. */
static void quiet_collects_whatever_the_growth_and_reports_what_it_freed(void)
{
    static uintptr_t values[4096];
    Roots roots = {values, 0};
    NetWasmGc *gc = create(4 * MIB, &roots);
    /* Nothing to trace or free. */
    assert(netwasm_gc_collect_when_quiet(gc) == 0);
    assert(netwasm_gc_collection_count(gc) == 0);
    /* Half a megabyte of garbage is too little growth for an idle collection; a quiet one frees all of it. */
    fill(gc, &roots, MIB / 2, 0, 1);
    assert(!netwasm_gc_collect_when_idle(gc));
    assert(netwasm_gc_collect_when_quiet(gc) == 256);
    assert(netwasm_gc_collection_count(gc) == 1);
    assert(netwasm_gc_collect_when_quiet(gc) == 0);
    /* A megabyte that stays and a megabyte that does not: about half is freed. */
    fill(gc, &roots, MIB, 1, 1);
    fill(gc, &roots, MIB, 0, 1);
    unsigned half = netwasm_gc_collect_when_quiet(gc);
    assert(half > 1 + 255 / 2 - 8 && half < 1 + 255 / 2 + 8);
    /* What stays alone: the collection traces it and frees nothing. */
    assert(netwasm_gc_collect_when_quiet(gc) == 1);
    assert(netwasm_gc_collection_count(gc) == 3);
    /* What the program then drops, without allocating, is freed by the next quiet collection. */
    roots.count = 0;
    assert(netwasm_gc_collect_when_quiet(gc) == 256);
    assert(netwasm_gc_collect_when_quiet(NULL) == 0);
    /* Without generations there is one, which every collection collects, and an object stays in it. */
    assert(netwasm_gc_oldest_generation_collection_count(gc) == netwasm_gc_collection_count(gc));
    assert(netwasm_gc_oldest_generation_collection_count(NULL) == 0);
    void *object = NULL;
    assert(test_allocate(gc, OBJECT_BYTES, 1, NULL, &object) == NETWASM_GC_OK);
    roots.values[roots.count++] = (uintptr_t)object;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_generation(gc, (uintptr_t)object) == 0);
    assert(netwasm_gc_object_generation(NULL, (uintptr_t)object) == 0);
    netwasm_gc_destroy(gc);
}

int main(void)
{
    garbage_collects_at_the_minimum_budget();
    steady_heap_grows_to_twice_the_survivors();
    growing_heap_grows_to_three_times_the_survivors();
    small_objects_refill_their_peak();
    large_objects_do_not_raise_the_peak();
    no_memory_for_a_block_collects_and_retries();
    no_memory_for_a_large_object_collects_and_retries();
    idle_collects_once_the_heap_has_grown_enough();
    quiet_collects_whatever_the_growth_and_reports_what_it_freed();
    assert(outstanding_bytes == 0);
    return 0;
}
