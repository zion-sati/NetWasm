// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT
#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"
#include "netwasm_gc_test_layouts.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>

#define OBJECTS 4096u
#define PAYLOAD 40u

static size_t backing_allocations;
static uintptr_t roots[OBJECTS];
static size_t root_count;
static const size_t reference_offsets[] = { 0 };
static const NetWasmGcDescriptor referencing = { reference_offsets, 1 };

static void *allocate_memory(size_t bytes, size_t alignment)
{
    assert(alignment <= _Alignof(max_align_t));
    backing_allocations++;
    /* Backing memory is not assumed to be zero. */
    void *result = malloc(bytes);
    assert(result != NULL);
    memset(result, 0x5c, bytes);
    return result;
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

static unsigned char *allocate(NetWasmGc *gc, size_t bytes)
{
    void *result = test_try_allocate_available(gc, 1, bytes, &referencing);
    if (result == NULL) assert(test_allocate(gc, 1, bytes, &referencing, &result) == NETWASM_GC_OK);
    return result;
}

static void assert_zero(const unsigned char *payload, size_t bytes)
{
    for (size_t index = 0; index < bytes; index++) assert(payload[index] == 0);
}

static void fill(unsigned char *payload, size_t bytes, unsigned char value)
{
    /* The first word is the object's reference field; keep it null. */
    memset(payload + sizeof(uintptr_t), value, bytes - sizeof(uintptr_t));
}

static int compare_addresses(const void *left, const void *right)
{
    uintptr_t a = *(const uintptr_t *)left;
    uintptr_t b = *(const uintptr_t *)right;
    return (a > b) - (a < b);
}

/* Live objects never overlap: sorted bases are at least a payload apart. */
static void assert_disjoint(uintptr_t *addresses, size_t count)
{
    qsort(addresses, count, sizeof(*addresses), compare_addresses);
    for (size_t index = 1; index < count; index++) assert(addresses[index] - addresses[index - 1] >= PAYLOAD);
}

static void holes_are_reused_cleared_and_survivors_kept(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, SIZE_MAX / 4, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, NULL);
    static unsigned char *objects[OBJECTS];
    root_count = 0;
    for (size_t index = 0; index < OBJECTS; index++) {
        objects[index] = allocate(gc, PAYLOAD);
        assert_zero(objects[index], PAYLOAD);
        fill(objects[index], PAYLOAD, (unsigned char)(index % 3 == 0 ? 0xa1 : 0xd7));
        if (index % 3 == 0) roots[root_count++] = (uintptr_t)objects[index];
    }
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == root_count);

    /* Fill exactly the holes: every new object is a cleared former slot, and no
     * backing memory is requested. */
    size_t backing_before = backing_allocations;
    size_t holes = OBJECTS - root_count;
    static uintptr_t addresses[OBJECTS];
    for (size_t index = 0; index < holes; index++) {
        unsigned char *object = allocate(gc, PAYLOAD);
        assert_zero(object, PAYLOAD);
        bool former = false;
        for (size_t candidate = 0; candidate < OBJECTS && !former; candidate++)
            former = object == objects[candidate] && candidate % 3 != 0;
        assert(former);
        assert(netwasm_gc_base(gc, (uintptr_t)object + 1) == (uintptr_t)object);
        fill(object, PAYLOAD, 0x3e);
        addresses[index] = (uintptr_t)object;
    }
    assert(backing_allocations == backing_before);
    for (size_t index = 0; index < root_count; index++) {
        const unsigned char *survivor = (const unsigned char *)roots[index];
        for (size_t byte = sizeof(uintptr_t); byte < PAYLOAD; byte++) assert(survivor[byte] == 0xa1);
        addresses[holes + index] = roots[index];
    }
    assert_disjoint(addresses, OBJECTS);

    /* The next allocation has no hole left and takes fresh space. */
    unsigned char *fresh = allocate(gc, PAYLOAD);
    assert_zero(fresh, PAYLOAD);
    for (size_t index = 0; index < OBJECTS; index++) assert((uintptr_t)fresh != addresses[index]);
    netwasm_gc_destroy(gc);
}

static void untaken_recycled_slots_are_not_objects(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, SIZE_MAX / 4, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, NULL);
    static unsigned char *objects[64];
    root_count = 0;
    for (size_t index = 0; index < 64; index++) {
        objects[index] = allocate(gc, PAYLOAD);
        if (index % 2 == 0) roots[root_count++] = (uintptr_t)objects[index];
    }
    netwasm_gc_collect(gc);
    /* Taking one recycled slot loads a word of them; the rest stay free. */
    unsigned char *taken = allocate(gc, PAYLOAD);
    assert(netwasm_gc_contains(gc, (uintptr_t)taken));
    size_t free_slots = 0;
    for (size_t index = 1; index < 64; index += 2) {
        if (objects[index] == taken) continue;
        free_slots++;
        assert(!netwasm_gc_contains(gc, (uintptr_t)objects[index]));
        assert(netwasm_gc_base(gc, (uintptr_t)objects[index] + 1) == 0);
    }
    assert(free_slots == 31);
    /* A collection while slots are loaded leaves them free and keeps survivors. */
    netwasm_gc_collect(gc);
    assert(netwasm_gc_object_count(gc) == root_count);
    assert(!netwasm_gc_contains(gc, (uintptr_t)taken));
    netwasm_gc_destroy(gc);
}

static void repeated_sweeps_keep_objects_disjoint(void)
{
    NetWasmGc *gc = NULL;
    assert(netwasm_gc_create(allocate_memory, release_memory, 64 * 1024, &gc) == NETWASM_GC_OK);
    netwasm_gc_set_root_tracer(gc, trace_roots, NULL);
    static uintptr_t addresses[OBJECTS];
    root_count = 0;
    unsigned random = 12345;
    for (size_t round = 0; round < 40; round++) {
        /* Keep a random subset of the current roots, then allocate past the
         * collection budget so sweeps run while recycled words are loaded. */
        size_t kept = 0;
        for (size_t index = 0; index < root_count; index++) {
            random = random * 1103515245u + 12345u;
            if ((random >> 16) % 4 != 0) roots[kept++] = roots[index];
        }
        root_count = kept;
        for (size_t index = 0; index < 600 && root_count < OBJECTS; index++) {
            random = random * 1103515245u + 12345u;
            unsigned char *object = allocate(gc, PAYLOAD + 16 * ((random >> 16) % 3));
            assert_zero(object, PAYLOAD);
            object[PAYLOAD - 1] = (unsigned char)round;
            if ((random >> 20) % 2 == 0) roots[root_count++] = (uintptr_t)object;
        }
        netwasm_gc_collect(gc);
        assert(netwasm_gc_object_count(gc) == root_count);
        memcpy(addresses, roots, root_count * sizeof(*roots));
        assert_disjoint(addresses, root_count);
    }
    netwasm_gc_destroy(gc);
}

int main(void)
{
    holes_are_reused_cleared_and_survivors_kept();
    untaken_recycled_slots_are_not_objects();
    repeated_sweeps_keep_objects_disjoint();
    return 0;
}
