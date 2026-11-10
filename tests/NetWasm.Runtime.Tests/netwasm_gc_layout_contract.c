/* SPDX-License-Identifier: MIT */
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.c"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>

static void all_size_classes_fit(void)
{
    for (size_t stride = NWGC_ALIGNMENT; stride <= NWGC_SMALL_LIMIT; stride += NWGC_ALIGNMENT) {
        size_t words, offset;
        size_t slots = block_slot_count(stride, &words, &offset);
        assert(slots > 0);
        assert(offset % NWGC_ALIGNMENT == 0);
        assert(words * NWGC_WORD_BITS >= slots);
        assert(offset >= sizeof(NetWasmGcBlock) + words * sizeof(uintptr_t) * 3);
        assert(offset + slots * stride <= NWGC_BLOCK_BYTES);
        size_t next_words = (slots + 1 + NWGC_WORD_BITS - 1) / NWGC_WORD_BITS;
        size_t next_offset;
        assert(align_size(sizeof(NetWasmGcBlock) + next_words * sizeof(uintptr_t) * 3,
            NWGC_ALIGNMENT, &next_offset));
        assert(next_offset + (slots + 1) * stride > NWGC_BLOCK_BYTES);
    }
}

static void *page_aligned_backing(size_t bytes, size_t alignment)
{
    assert(alignment <= NWGC_PAGE_BYTES);
    size_t rounded;
    assert(align_size(bytes, NWGC_PAGE_BYTES, &rounded));
    return aligned_alloc(NWGC_PAGE_BYTES, rounded);
}

static void release_backing(void *value, size_t bytes, size_t alignment)
{
    (void)bytes;
    (void)alignment;
    free(value);
}

static void trace_root(NetWasmGc *gc, void *state)
{
    netwasm_gc_mark(gc, *(uintptr_t *)state);
}

typedef struct {
    uintptr_t *values;
    size_t count;
} LayoutRoots;

static NetWasmGcBlock *object_block(NetWasmGc *gc, void *object)
{
    NetWasmGcRegion *region = find_page(gc, (uintptr_t)object >> NWGC_PAGE_SHIFT)->regions[0];
    assert(region->kind == NWGC_REGION_BLOCK);
    return (NetWasmGcBlock *)region;
}

static void trace_roots(NetWasmGc *gc, void *state)
{
    LayoutRoots *roots = state;
    for (size_t index = 0; index < roots->count; index++)
        netwasm_gc_mark(gc, roots->values[index]);
}

static void page_boundary_endpoint_keeps_owner(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, 1024 * 1024, &gc) == NETWASM_GC_OK);
    size_t bytes = 2 * NWGC_PAGE_BYTES - sizeof(NetWasmGcLarge);
    void *object = NULL;
    assert(test_allocate(gc, 1, bytes, NULL, &object) == NETWASM_GC_OK);
    uintptr_t endpoint = (uintptr_t)object + bytes;
    assert((endpoint & (NWGC_PAGE_BYTES - 1)) == 0);
    assert(netwasm_gc_base(gc, endpoint) == (uintptr_t)object);
    assert(netwasm_gc_base(gc, endpoint + 1) == 0);
    uintptr_t root = endpoint;
    netwasm_gc_set_root_tracer(gc, trace_root, &root);
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 1);
    assert(netwasm_gc_live_bytes(gc) == bytes);

    static const size_t offsets[] = {0};
    static const NetWasmGcDescriptor reference = {offsets, 1};
    void *owner = NULL;
    assert(test_allocate(gc, 1, sizeof(uintptr_t), &reference, &owner) == NETWASM_GC_OK);
    *(uintptr_t *)owner = endpoint;
    root = (uintptr_t)owner;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 2);
    assert(netwasm_gc_base(gc, endpoint) == (uintptr_t)object);
    root = 0;
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == 0);
    netwasm_gc_destroy(gc);
}

static void word_run_operations_cross_bitmap_boundaries(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, SIZE_MAX, &gc) == NETWASM_GC_OK);
    NetWasmGcBlock *block = NULL;
    assert(create_block(gc, NWGC_ALIGNMENT, false, &block) == NETWASM_GC_OK);
    assert(block->slot_count > NWGC_WORD_BITS + 3);

    uintptr_t *allocated = block_allocated(block);
    bitmap_set_range(allocated, 0, block->slot_count);
    bitmap_clear_range(allocated, NWGC_WORD_BITS - 2, NWGC_WORD_BITS + 3);
    block->search = NWGC_WORD_BITS - 4;
    size_t end = 0;
    size_t first = find_free_run(gc, block, NWGC_WORD_BITS, &end);
    assert(first == NWGC_WORD_BITS - 2);
    assert(end == NWGC_WORD_BITS + 3);

    bitmap_set_range(allocated, first, end);
    for (size_t slot = first; slot < end; slot++) assert(bit_is_set(allocated, slot));
    bitmap_clear_range(allocated, first, end);
    for (size_t slot = first; slot < end; slot++) assert(!bit_is_set(allocated, slot));
    netwasm_gc_destroy(gc);
}

static void segmented_directory_separates_radix_paths(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, SIZE_MAX, &gc) == NETWASM_GC_OK);
    uintptr_t adjacent_leaf = NWGC_DIRECTORY_FANOUT;
    uintptr_t distant_root = (uintptr_t)2 <<
        ((NWGC_DIRECTORY_LEVELS - 1) * NWGC_DIRECTORY_BITS);
    NetWasmGcPage *zero = NULL;
    NetWasmGcPage *adjacent = NULL;
    NetWasmGcPage *distant = NULL;
    assert(directory_page(gc, 0, true, &zero) == NETWASM_GC_OK);
    assert(directory_page(gc, adjacent_leaf, true, &adjacent) == NETWASM_GC_OK);
    assert(directory_page(gc, distant_root, true, &distant) == NETWASM_GC_OK);
    assert(zero != adjacent && zero != distant && adjacent != distant);
    NetWasmGcRegion regions[3] = {0};
    zero->regions[0] = &regions[0];
    adjacent->regions[0] = &regions[1];
    distant->regions[0] = &regions[2];
    assert(find_page(gc, 0)->regions[0] == &regions[0]);
    assert(find_page(gc, adjacent_leaf)->regions[0] == &regions[1]);
    assert(find_page(gc, distant_root)->regions[0] == &regions[2]);
    assert(find_page_cached(gc, 0) == zero);
    assert(find_page_cached(gc, 0) == zero);
    netwasm_gc_destroy(gc);
}

static void reserved_run_tail_is_unpublished_and_retired(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, SIZE_MAX, &gc) == NETWASM_GC_OK);
    void *object = NULL;
    assert(test_allocate(gc, 1, 1, NULL, &object) == NETWASM_GC_OK);
    NetWasmGcBlock *block = object_block(gc, object);
    NetWasmGcRun *run = &gc->runs[block->class_index][block->traced];
    assert(block_class(gc, block)->run_block == block);
    assert(run->cursor < run->end);
    uintptr_t reserved_payload = (uintptr_t)run->cursor;
    assert(!netwasm_gc_contains(gc, reserved_payload));
    /* The whole run is charged to the heap when it is reserved. */
    assert(gc->occupied_bytes > block->slot_bytes);

    uintptr_t root = (uintptr_t)object;
    netwasm_gc_set_root_tracer(gc, trace_root, &root);
    netwasm_gc_collect(gc);
    /* Collection retires the run; only the live slot stays charged. */
    assert(gc->occupied_bytes == block->slot_bytes);
    assert(netwasm_gc_object_count(gc) == 1);
    assert(block_class(gc, block)->available == block);
    netwasm_gc_destroy(gc);
}

static void full_blocks_stay_off_available_lists(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, SIZE_MAX, &gc) == NETWASM_GC_OK);
    void *first = NULL;
    assert(test_allocate(gc, 1, 1, NULL, &first) == NETWASM_GC_OK);
    NetWasmGcBlock *block = object_block(gc, first);
    size_t count = block->slot_count;
    uintptr_t *roots = malloc(count * sizeof(*roots));
    assert(roots != NULL);
    roots[0] = (uintptr_t)first;
    for (size_t index = 1; index < count; index++) {
        void *object = NULL;
        assert(test_allocate(gc, 1, 1, NULL, &object) == NETWASM_GC_OK);
        roots[index] = (uintptr_t)object;
    }
    LayoutRoots root_set = {roots, count};
    netwasm_gc_set_root_tracer(gc, trace_roots, &root_set);
    netwasm_gc_collect(gc);
    assert(block->live == block->slot_count);
    assert(block_class(gc, block)->available == NULL);
    free(roots);
    netwasm_gc_destroy(gc);
}

static void full_collection_preserves_the_floor_above_twice_live_slots(void)
{
    const size_t floor = 1024 * 1024;
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, floor, &gc) == NETWASM_GC_OK);
    void *object = NULL;
    assert(test_allocate(gc, 1, 1, NULL, &object) == NETWASM_GC_OK);
    uintptr_t root = (uintptr_t)object;
    netwasm_gc_set_root_tracer(gc, trace_root, &root);
    netwasm_gc_collect(gc);
    assert(gc->next_collection_charge == gc->occupied_bytes * 2 + floor);
    netwasm_gc_destroy(gc);
}

static void empty_blocks_leave_and_reenter_the_active_list(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(page_aligned_backing, release_backing, SIZE_MAX, &gc) == NETWASM_GC_OK);
    void *object = NULL;
    assert(test_allocate(gc, 1, 1, NULL, &object) == NETWASM_GC_OK);
    NetWasmGcBlock *block = object_block(gc, object);
    assert(gc->blocks == block);
    netwasm_gc_collect(gc);
    assert(gc->blocks == NULL);
    assert(gc->empty_blocks == block);

    assert(test_allocate(gc, 1, 1, NULL, &object) == NETWASM_GC_OK);
    assert(object_block(gc, object) == block);
    assert(gc->blocks == block);
    assert(gc->empty_blocks == NULL);
    netwasm_gc_destroy(gc);
}

int main(void)
{
    all_size_classes_fit();
    page_boundary_endpoint_keeps_owner();
    word_run_operations_cross_bitmap_boundaries();
    segmented_directory_separates_radix_paths();
    reserved_run_tail_is_unpublished_and_retired();
    full_blocks_stay_off_available_lists();
    full_collection_preserves_the_floor_above_twice_live_slots();
    empty_blocks_leave_and_reenter_the_active_list();
    return 0;
}
