/* SPDX-License-Identifier: MIT */
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>

/* The memory the collector takes from its host comes filled with a pattern, so what an
 * allocation leaves uncleared shows. A zeroed allocation reads zero; an uninitialized one
 * keeps the pattern wherever the collector does not clear in bulk: Compact's objects and
 * NetWasmGC's large ones (NetWasmGC's small objects come from runs, zeroed when they open). */
#define PATTERN 0xCD

typedef union {
    struct {
        size_t bytes;
        size_t alignment;
    } fields;
    max_align_t alignment;
} AllocationHeader;

static size_t allocations;
static size_t releases;

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    AllocationHeader *header = malloc(sizeof(*header) + bytes);
    assert(header != NULL);
    header->fields.bytes = bytes;
    header->fields.alignment = alignment;
    memset(header + 1, PATTERN, bytes);
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

static bool filled_with(const unsigned char *bytes, size_t count, unsigned char value)
{
    for (size_t index = 0; index < count; index++)
        if (bytes[index] != value) return false;
    return true;
}

static void no_roots(NetWasmGc *gc, void *state)
{
    (void)gc;
    (void)state;
}

int main(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, SIZE_MAX, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, no_roots, NULL);
    const size_t sizes[] = {24, 3000, 4000, 100000, (size_t)1 << 20};
    for (size_t round = 0; round < 3; round++) {
        for (size_t index = 0; index < sizeof(sizes) / sizeof(sizes[0]); index++) {
            size_t bytes = sizes[index];
            void *zeroed = NULL;
            void *uninitialized = NULL;
            assert(netwasm_gc_allocate(gc, bytes, false, &zeroed) == NETWASM_GC_OK);
            test_record(zeroed, bytes, 1, NULL);
            assert(filled_with(zeroed, bytes, 0));
            assert(netwasm_gc_allocate_uninitialized(gc, bytes, &uninitialized) == NETWASM_GC_OK);
            test_record(uninitialized, bytes, 1, NULL);
            assert(uninitialized != zeroed && netwasm_gc_contains(gc, (uintptr_t)uninitialized));
#ifdef NETWASM_GC_COMPACT
            bool cleared = false;
#else
            bool cleared = bytes <= 4096 - 16;
#endif
            /* The first round's memory is fresh from the host; later rounds may reuse what a
             * collection freed, which held the pattern or what the program wrote. */
            if (round == 0) assert(filled_with(uninitialized, bytes, cleared ? 0 : PATTERN));
            memset(uninitialized, 0x11, bytes);
            memset(zeroed, 0x22, bytes);
        }
        /* Nothing is rooted, so a collection frees every object, uninitialized ones included. */
        netwasm_gc_collect(gc);
        assert(netwasm_gc_object_count(gc) == 0);
    }
    netwasm_gc_destroy(gc);
    assert(allocations == releases);
    return 0;
}
