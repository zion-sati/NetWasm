// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>

typedef union {
    struct {
        size_t bytes;
        size_t alignment;
    } fields;
    max_align_t alignment;
} AllocationHeader;

typedef struct {
    uintptr_t *values;
    size_t count;
} Roots;

static size_t allocations;
static size_t releases;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    AllocationHeader *header = malloc(sizeof(*header) + bytes);
    assert(header != NULL);
    header->fields.bytes = bytes;
    header->fields.alignment = alignment;
    allocations++;
    return header + 1;
}

static void release_memory(void *address, size_t bytes, size_t alignment)
{
    AllocationHeader *header = (AllocationHeader *)address - 1;
    assert(header->fields.bytes == bytes);
    assert(header->fields.alignment == alignment);
    releases++;
    free(header);
}

static void trace_roots(NetWasmGc *gc, void *state)
{
    Roots *roots = state;
    for (size_t index = 0; index < roots->count; index++)
        netwasm_gc_mark(gc, roots->values[index]);
}

static void *allocate_object(
    NetWasmGc *gc,
    size_t count,
    size_t size,
    const NetWasmGcDescriptor *descriptor)
{
    void *result = NULL;
    assert(test_allocate(gc, count, size, descriptor, &result) == NETWASM_GC_OK);
    assert(result != NULL);
    return result;
}

static void roots_zeroing_and_reuse(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    Roots roots = {0};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);

    unsigned char *live = allocate_object(gc, 37, 1, NULL);
    unsigned char *dead = allocate_object(gc, 37, 1, NULL);
    for (size_t index = 0; index < 37; index++) assert(live[index] == 0 && dead[index] == 0);
    memset(live, 0xa5, 37);
    uintptr_t values[] = {(uintptr_t)live};
    roots = (Roots){values, 1};
    netwasm_gc_collect(gc);
    assert(netwasm_gc_contains(gc, (uintptr_t)live));
    assert(!netwasm_gc_contains(gc, (uintptr_t)dead));
    assert(netwasm_gc_object_count(gc) == 1);
    roots.count = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);

    unsigned char *replacement = allocate_object(gc, 91, 1, NULL);
    for (size_t index = 0; index < 91; index++) assert(replacement[index] == 0);
    netwasm_gc_destroy(gc);
}

static void exact_graph_and_cycle(void)
{
    static const size_t offsets[] = {0};
    static const NetWasmGcDescriptor reference = {offsets, 1};
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    Roots roots = {0};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);

    uintptr_t *left = allocate_object(gc, 1, sizeof(uintptr_t), &reference);
    uintptr_t *right = allocate_object(gc, 1, sizeof(uintptr_t), &reference);
    *left = (uintptr_t)right;
    *right = (uintptr_t)left;
    uintptr_t values[] = {(uintptr_t)left};
    roots = (Roots){values, 1};
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 2);
    assert(netwasm_gc_is_marked(gc, (uintptr_t)left) == false);
    roots.count = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);

    /* Without generations the youngest generation is every object, so GC.Collect(0) collects them all. */
    left = allocate_object(gc, 1, sizeof(uintptr_t), &reference);
    uint64_t collections = netwasm_gc_collection_count(gc);
    netwasm_gc_collect_young(gc);
    assert(netwasm_gc_object_count(gc) == 0 && netwasm_gc_collection_count(gc) == collections + 1);
    netwasm_gc_destroy(gc);
}

static void managed_interior_reference_fallback(void)
{
    static const size_t offsets[] = {0};
    static const NetWasmGcDescriptor reference = {offsets, 1};
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    Roots roots = {0};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);

    uintptr_t *owner = allocate_object(gc, 1, sizeof(uintptr_t), &reference);
    unsigned char *leaf = allocate_object(gc, 64, 1, NULL);
    *owner = (uintptr_t)(leaf + 17);
    uintptr_t values[] = {(uintptr_t)owner};
    roots = (Roots){values, 1};
    netwasm_gc_collect(gc);
    assert(netwasm_gc_base(gc, *owner) == (uintptr_t)leaf);
    assert(netwasm_gc_object_count(gc) == 2);

    *owner = (uintptr_t)(leaf + 64);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_contains(gc, *owner));
    assert(netwasm_gc_object_count(gc) == 2);
    netwasm_gc_destroy(gc);
}

static void interior_endpoint_and_large(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    Roots roots = {0};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);

    unsigned char *small = allocate_object(gc, 64, 1, NULL);
    uintptr_t values[] = {(uintptr_t)(small + 17)};
    roots = (Roots){values, 1};
    netwasm_gc_collect(gc);
    assert(netwasm_gc_base(gc, (uintptr_t)(small + 17)) == (uintptr_t)small);
    values[0] = (uintptr_t)(small + 64);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_contains(gc, values[0]));

    unsigned char *large = allocate_object(gc, 5000, 1, NULL);
    values[0] = (uintptr_t)(large + 4097);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_base(gc, values[0]) == (uintptr_t)large);
    assert(!netwasm_gc_contains(gc, (uintptr_t)&roots));
    roots.count = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);
    netwasm_gc_destroy(gc);
}

static void zero_sized_and_layout_validation(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    Roots roots = {0};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);
    void *first = allocate_object(gc, 0, 1, NULL);
    void *second = allocate_object(gc, 0, 1, NULL);
    assert(first != second);
    uintptr_t values[] = {(uintptr_t)first};
    roots = (Roots){values, 1};
    netwasm_gc_collect(gc);
    assert(netwasm_gc_contains(gc, (uintptr_t)first));
    assert(!netwasm_gc_contains(gc, (uintptr_t)second));

    netwasm_gc_destroy(gc);
}

static void bitmap_word_sweep_and_padding(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    uintptr_t objects[130];
    uintptr_t values[65];
    for (size_t index = 0; index < 130; index++) {
        objects[index] = (uintptr_t)allocate_object(gc, 1, 1, NULL);
        if ((index & 1) == 0) values[index / 2] = objects[index];
    }
    Roots roots = {values, 65};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 65);
    for (size_t index = 0; index < 130; index++)
        assert(netwasm_gc_contains(gc, objects[index]) == ((index & 1) == 0));
    netwasm_gc_destroy(gc);
}

static void pending_bitmap_requeues_backward_edges(void)
{
    static const size_t offsets[] = {0};
    static const NetWasmGcDescriptor reference = {offsets, 1};
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    size_t count = sizeof(uintptr_t) * 8 + 1;
    uintptr_t *objects[sizeof(uintptr_t) * 8 + 1];
    for (size_t index = 0; index < count; index++)
        objects[index] = allocate_object(gc, 1, sizeof(uintptr_t), &reference);
    *objects[count - 1] = (uintptr_t)objects[0];
    uintptr_t values[] = {(uintptr_t)objects[count - 1]};
    Roots roots = {values, 1};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 2);
    assert(netwasm_gc_contains(gc, (uintptr_t)objects[0]));
    assert(netwasm_gc_contains(gc, (uintptr_t)objects[count - 1]));
    netwasm_gc_destroy(gc);
}

static void large_pending_object_traces_references(void)
{
    static const size_t offsets[] = {0};
    static const NetWasmGcDescriptor reference = {offsets, 1};
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 1024 * 1024, &gc) == NETWASM_GC_OK);
    uintptr_t *large = allocate_object(gc, 600, sizeof(uintptr_t), &reference);
    void *leaf = allocate_object(gc, 17, 1, NULL);
    large[599] = (uintptr_t)leaf;
    uintptr_t values[] = {(uintptr_t)large};
    Roots roots = {values, 1};
    netwasm_gc_set_root_tracer(gc, trace_roots, &roots);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 2);
    assert(netwasm_gc_contains(gc, (uintptr_t)leaf));
    netwasm_gc_destroy(gc);
}

/* NetWasmGC reserves arenas of 16 blocks, doubling to 256. Compact takes one
 * 64 KiB chunk at a time: 21 slots of 3008 bytes fit, so 6000 need 286. */
static void storage_growth(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, SIZE_MAX, &gc) == NETWASM_GC_OK);
    for (size_t index = 0; index < 6000; index++)
        (void)allocate_object(gc, 3000, 1, NULL);
    uint64_t backing = netwasm_gc_reserved_bytes(gc);
#ifdef NETWASM_GC_COMPACT
    /* Each chunk adds its header and start bitmap, at most 2 KiB. */
    assert(backing >= 286u * 64u * 1024u);
    assert(backing <= 286u * 66u * 1024u);
#else
    assert(backing >= 31u * 1024u * 1024u);
    assert(backing < 32u * 1024u * 1024u);
#endif
    netwasm_gc_destroy(gc);
}

int main(void)
{
    roots_zeroing_and_reuse();
    exact_graph_and_cycle();
    managed_interior_reference_fallback();
    interior_endpoint_and_large();
    zero_sized_and_layout_validation();
    bitmap_word_sweep_and_padding();
    pending_bitmap_requeues_backward_edges();
    large_pending_object_traces_references();
    storage_growth();
    assert(allocations == releases);
    return 0;
}
