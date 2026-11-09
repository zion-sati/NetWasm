/* SPDX-License-Identifier: MIT */
#include <assert.h>
#ifndef CLONE_TEST_HOOKS
#define NETWASM_RUNTIME_PACK
#endif
#define NETWASM_UNMANAGED_ALLOCATOR_BACKEND "unmanaged_allocator_libc.h"
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wattributes"
#include "../../src/NetWasm.Runtime/native_runtime.c"
#pragma GCC diagnostic pop
#include "../../src/NetWasm.Runtime/collector/netwasm/runtime_state.h"

void runtime_initialize_libc(void) { }
void runtime_initialize_system(void) { }
static size_t finalized_clones;

static void mark_word(uintptr_t *bitmap, size_t bit)
{
    *bitmap |= (uintptr_t)1 << bit;
}
u32 netwasm_managed_finalize(netwasm_reference_t object, u32 type_id)
{
    assert(object != 0 && type_id == 3);
    finalized_clones++;
    return 0;
}

int main(void)
{
    initialize(0, 8, 8);
    initialize_finalization();
    uintptr_t bitmap = 0;
    mark_word(&bitmap, 1);
    register_type(1, 0, 3 * sizeof(uintptr_t), (uintptr_t)&bitmap, 3, 0, 0, 0, 0);
    register_type(2, 0, sizeof(uintptr_t), 0, 0, 0, 0, 0, 0);
    register_type(3, 0, 3 * sizeof(uintptr_t), (uintptr_t)&bitmap, 3, 0, 0, 1, 0);
    uintptr_t array_bitmap = 0;
    mark_word(&array_bitmap, NETWASM_ARRAY_DATA_POINTER_OFFSET / sizeof(uintptr_t));
    register_type(4, 0, NETWASM_ARRAY_OBJECT_SIZE, (uintptr_t)&array_bitmap,
        NETWASM_ARRAY_OBJECT_SIZE / sizeof(uintptr_t), 0, 0, 0, 0);
    register_type(5, 0, netwasm_align_address(NETWASM_STRING_MIN_OBJECT_SIZE,
        sizeof(uintptr_t)), 0, 0, 0, 0, 0, 0);
    register_type(6, 0, 3 * sizeof(uintptr_t), (uintptr_t)&bitmap, 3, 0, 0, 0, 0);
    uintptr_t value_bitmap = 0;
    mark_word(&value_bitmap, 0);
    register_value_type(6, 2 * sizeof(uintptr_t), sizeof(uintptr_t),
        (uintptr_t)&value_bitmap, 2);
    register_type(7, 0, NETWASM_ARRAY_OBJECT_SIZE, (uintptr_t)&array_bitmap,
        NETWASM_ARRAY_OBJECT_SIZE / sizeof(uintptr_t), 0, 0, 0, 0);
    uintptr_t roots[4] = {0};
    collector_register_roots(roots, sizeof(roots));
    roots[0] = allocate(3 * sizeof(uintptr_t), 1);
    uintptr_t child = allocate(sizeof(uintptr_t), 2);
    collector_store_reference(roots[0], (uintptr_t *)roots[0] + 1, child);
    ((uintptr_t *)roots[0])[2] = 12345;
    u32 source_hash = object_identity_hash(roots[0]);
#ifdef CLONE_TEST_HOOKS
    fail_next_allocation = 1;
    assert(object_clone(roots[0]) == 0);
    collect_before_allocation = 1;
#endif
    roots[1] = object_clone(roots[0]);
    assert(roots[1] != roots[0]);
    assert(((uintptr_t *)roots[1])[1] == child);
    assert(((uintptr_t *)roots[1])[2] == 12345);
    assert(object_identity_hash(roots[0]) == source_hash);
    assert(object_identity_hash(roots[1]) == object_identity_hash(roots[1]));
    ((uintptr_t *)roots[1])[2] = 99;
    assert(((uintptr_t *)roots[0])[2] == 12345);
    roots[0] = 0;
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, child));
    assert(((uintptr_t *)roots[1])[1] == child);

    roots[0] = allocate_reference_array(2, 4, 2);
    uintptr_t data = *(uintptr_t *)(roots[0] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    collector_store_reference(roots[0], (uintptr_t *)data, child);
    roots[2] = object_clone(roots[0]);
    uintptr_t clone_data = *(uintptr_t *)(roots[2] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    assert(clone_data != data && ((uintptr_t *)clone_data)[0] == child);
    collector_store_reference(roots[2], (uintptr_t *)clone_data, 0);
    assert(((uintptr_t *)data)[0] == child);

    roots[0] = allocate(3 * sizeof(uintptr_t), 6);
    collector_store_reference(roots[0], (uintptr_t *)roots[0] + 1, child);
    ((uintptr_t *)roots[0])[2] = 321;
    roots[2] = object_clone(roots[0]);
    assert(roots[0] != roots[2] && *(u32 *)roots[2] == 6);
    assert(((uintptr_t *)roots[2])[1] == child && ((uintptr_t *)roots[2])[2] == 321);
    roots[0] = allocate_value_array(2, 7, 6, 2 * sizeof(uintptr_t));
    data = *(uintptr_t *)(roots[0] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    collector_store_reference(roots[0], (uintptr_t *)data, child);
    ((uintptr_t *)data)[1] = 456;
    roots[2] = object_clone(roots[0]);
    clone_data = *(uintptr_t *)(roots[2] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    assert(data != clone_data && ((uintptr_t *)clone_data)[0] == child);
    assert(((uintptr_t *)clone_data)[1] == 456);
    ((uintptr_t *)clone_data)[1] = 789;
    assert(((uintptr_t *)data)[1] == 456);
    collector_collect();
    assert(((uintptr_t *)clone_data)[0] == child);

    roots[0] = allocate_string('x', 3, 5);
    roots[2] = object_clone(roots[0]);
    assert(roots[0] != roots[2]);
    assert(*(u32 *)(roots[2] + NETWASM_STRING_LENGTH_OFFSET) == 3);
    assert(memcmp((void *)(roots[0] + NETWASM_STRING_DATA_OFFSET),
        (void *)(roots[2] + NETWASM_STRING_DATA_OFFSET), 6) == 0);

    roots[0] = allocate_finalizable(3 * sizeof(uintptr_t), 3);
    suppress_finalize(roots[0]);
    roots[3] = object_clone(roots[0]);
    roots[0] = 0;
    collector_collect();
    collector_invoke_finalizers();
    assert(finalized_clones == 0);
    roots[3] = 0;
    collector_collect();
    collector_invoke_finalizers();
    assert(finalized_clones == 1);
    roots[1] = roots[2] = 0;
    collector_collect();
    assert(!netwasm_gc_contains(netwasm_gc_runtime, child));
    return 0;
}
