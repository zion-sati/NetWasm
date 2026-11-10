/* SPDX-License-Identifier: MIT */
/* Behaviour particular to Compact's core (compact_gc.c): the mark stack's
 * rescan fallback, free-list reuse, and resolution through chunk bitmaps. */
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>

static size_t allocations, releases;
static bool fail_allocation;
static const size_t reference_offsets[] = {0};
static const NetWasmGcDescriptor reference = {reference_offsets, 1};

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    if (fail_allocation) return NULL;
    allocations++;
    void *result = malloc(bytes);
    /* Backing memory is not assumed to be zero. */
    if (result != NULL) memset(result, 0xa5, bytes);
    return result;
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    releases++;
    free(address);
}

typedef struct { const uintptr_t *values; size_t count; } Roots;

static void trace_roots(NetWasmGc *gc, void *state)
{
    const Roots *roots = state;
    for (size_t index = 0; index < roots->count; index++) netwasm_gc_mark(gc, roots->values[index]);
}

static NetWasmGc *create(Roots *roots)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, SIZE_MAX, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, roots);
    return gc;
}

static uintptr_t *allocate(NetWasmGc *gc, size_t words, const NetWasmGcDescriptor *descriptor)
{
    void *result = NULL;
    assert(test_allocate(gc, words, sizeof(uintptr_t), descriptor, &result) == NETWASM_GC_OK);
    for (size_t index = 0; index < words; index++) assert(((uintptr_t *)result)[index] == 0);
    return result;
}

/* A chain deeper than the first mark stack, marked once with the stack and
 * once with no memory for it at all. */
static void deep_chain_with_and_without_a_stack(void)
{
    uintptr_t head = 0;
    Roots roots = {&head, 1};
    NetWasmGc *gc = create(&roots);
    for (size_t index = 0; index < 5000; index++) {
        uintptr_t *node = allocate(gc, 1, &reference);
        node[0] = head;
        head = (uintptr_t)node;
    }
    (void)allocate(gc, 1, &reference);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 5000);
    /* Compact allocates from free lists: nothing outside it bumps a run. */
    assert(netwasm_gc_allocation_runs(gc) == NULL);
    netwasm_gc_destroy(gc);

    head = 0;
    gc = create(&roots);
    for (size_t index = 0; index < 5000; index++) {
        uintptr_t *node = allocate(gc, 1, &reference);
        node[0] = head;
        head = (uintptr_t)node;
    }
    (void)allocate(gc, 1, &reference);
    fail_allocation = true;
    netwasm_gc_collect(gc);
    fail_allocation = false;
    assert(netwasm_gc_object_count(gc) == 5000);
    head = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);
    netwasm_gc_destroy(gc);
}

static void freed_slots_are_reused_zeroed(void)
{
    uintptr_t kept[8];
    Roots roots = {kept, 0};
    NetWasmGc *gc = create(&roots);
    uintptr_t *objects[16];
    for (size_t index = 0; index < 16; index++) {
        objects[index] = allocate(gc, 3, NULL);
        objects[index][1] = 0x5a5a;
        if (index % 2 == 0) kept[roots.count++] = (uintptr_t)objects[index];
    }
    uint64_t reserved = netwasm_gc_reserved_bytes(gc);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 8);
    for (size_t index = 1; index < 16; index += 2) {
        assert(!netwasm_gc_contains(gc, (uintptr_t)objects[index]));
        assert(netwasm_gc_base(gc, (uintptr_t)objects[index] + 1) == 0);
    }
    for (size_t index = 0; index < 8; index++) {
        uintptr_t *object = allocate(gc, 3, NULL);
        bool former = false;
        for (size_t candidate = 1; candidate < 16 && !former; candidate += 2)
            former = object == objects[candidate];
        assert(former);
    }
    assert(netwasm_gc_reserved_bytes(gc) == reserved);
    for (size_t index = 0; index < 8; index++) assert(((uintptr_t *)kept[index])[1] == 0x5a5a);
    netwasm_gc_destroy(gc);
}

static void interior_and_endpoint_roots(void)
{
    uintptr_t values[3];
    Roots roots = {values, 3};
    NetWasmGc *gc = create(&roots);
    uintptr_t *small = allocate(gc, 4, NULL);
    uintptr_t *large = allocate(gc, 2000, &reference);
    uintptr_t *child = allocate(gc, 1, NULL);
    uintptr_t *dead = allocate(gc, 2000, NULL);
    large[1999] = (uintptr_t)child;
    values[0] = (uintptr_t)(small + 4);
    values[1] = (uintptr_t)large + 4321;
    values[2] = 0;
    assert(netwasm_gc_base(gc, values[0]) == (uintptr_t)small);
    assert(netwasm_gc_base(gc, (uintptr_t)(large + 2000)) == (uintptr_t)large);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 3);
    assert(netwasm_gc_contains(gc, (uintptr_t)child) && !netwasm_gc_contains(gc, (uintptr_t)dead));
    values[0] = values[1] = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);
    netwasm_gc_destroy(gc);
}

static void seeded_reachability(void)
{
    uint32_t seed = 1234567;
    for (int iteration = 0; iteration < 50; iteration++) {
        uintptr_t published[3];
        Roots roots = {published, 3};
        NetWasmGc *gc = create(&roots);
        uintptr_t *nodes[96];
        unsigned edges[96];
        bool reachable[96] = {0};
        for (int index = 0; index < 96; index++)
            nodes[index] = allocate(gc, 1 + (size_t)index % 5, &reference);
        for (int index = 0; index < 96; index++) {
            seed = seed * 1664525u + 1013904223u;
            edges[index] = seed % 97;
            nodes[index][0] = edges[index] < 96 ? (uintptr_t)nodes[edges[index]] : 0;
        }
        const unsigned starts[] = {0, 31, 80};
        for (size_t index = 0; index < 3; index++)
            for (unsigned node = starts[index]; node < 96 && !reachable[node]; node = edges[node])
                reachable[node] = true;
        published[0] = (uintptr_t)nodes[0];
        published[1] = (uintptr_t)nodes[31] + 1;
        published[2] = (uintptr_t)nodes[80];
        netwasm_gc_collect(gc);
        size_t expected = 0;
        for (int index = 0; index < 96; index++) {
            assert(netwasm_gc_contains(gc, (uintptr_t)nodes[index]) == reachable[index]);
            expected += reachable[index];
        }
        assert(netwasm_gc_object_count(gc) == expected);
        netwasm_gc_destroy(gc);
    }
}

int main(void)
{
    deep_chain_with_and_without_a_stack();
    freed_slots_are_reused_zeroed();
    interior_and_endpoint_roots();
    seeded_reachability();
    assert(allocations == releases);
    return 0;
}
