/* SPDX-License-Identifier: MIT */
#include <assert.h>
#define NETWASM_RUNTIME_PACK
#define NETWASM_UNMANAGED_ALLOCATOR_BACKEND "unmanaged_allocator_libc.h"
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wattributes"
#include "../../src/NetWasm.Runtime/native_runtime.c"
#pragma GCC diagnostic pop
#include "../../src/NetWasm.Runtime/collector/netwasm/runtime_state.h"
void runtime_initialize_libc(void) { }
void runtime_initialize_system(void) { }
u32 netwasm_managed_finalize(netwasm_reference_t object, u32 type_id)
{ (void)object; (void)type_id; assert(0); return 0; }

static void mark_word(uintptr_t *bitmap, size_t bit)
{
    *bitmap |= (uintptr_t)1 << bit;
}

int main(void)
{
    initialize(0, 8, 8);
    uintptr_t boxed = 0, payload = 0, array_bitmap = 0, nullable = 0;
    mark_word(&boxed, 1);
    mark_word(&payload, 0);
    mark_word(&nullable, 1);
    mark_word(&array_bitmap, NETWASM_ARRAY_DATA_POINTER_OFFSET / sizeof(uintptr_t));
    register_type(1, 0, sizeof(uintptr_t), 0, 0, 0, 0, 0, 0);
    register_type(2, 0, 3 * sizeof(uintptr_t), (uintptr_t)&boxed, 3, 0, 0, 0, 0);
    register_value_type(2, 2 * sizeof(uintptr_t), sizeof(uintptr_t), (uintptr_t)&payload, 2);
    register_type(3, 0, NETWASM_ARRAY_OBJECT_SIZE, (uintptr_t)&array_bitmap,
        NETWASM_ARRAY_OBJECT_SIZE / sizeof(uintptr_t), 0, 0, 0, 0);
    register_type(4, 0, NETWASM_ARRAY_OBJECT_SIZE, (uintptr_t)&array_bitmap,
        NETWASM_ARRAY_OBJECT_SIZE / sizeof(uintptr_t), 0, 0, 0, 0);
    uintptr_t nullable_box = 0;
    mark_word(&nullable_box, 2);
    register_type(5, 0, 4 * sizeof(uintptr_t), (uintptr_t)&nullable_box, 4, 0, 0, 0, 0);
    register_value_type(5, 3 * sizeof(uintptr_t), sizeof(uintptr_t), (uintptr_t)&nullable, 3);
    register_type(6, 0, NETWASM_ARRAY_OBJECT_SIZE, (uintptr_t)&array_bitmap,
        NETWASM_ARRAY_OBJECT_SIZE / sizeof(uintptr_t), 0, 0, 0, 0);
    uintptr_t roots[5] = {0};
    collector_register_roots(roots, sizeof(roots));
    roots[0] = allocate(sizeof(uintptr_t), 1);
    roots[1] = allocate(3 * sizeof(uintptr_t), 2);
    collector_store_reference(roots[1], (uintptr_t *)roots[1] + 1, roots[0]);
    ((uintptr_t *)roots[1])[2] = 42;
    roots[2] = allocate_value_array(2, 3, 2, 2 * sizeof(uintptr_t));
    uintptr_t data = *(uintptr_t *)(roots[2] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    assert(array_set_value(roots[2], 1, roots[1], 0, 0) == 0);
    assert(((uintptr_t *)data)[2] == roots[0] && ((uintptr_t *)data)[3] == 42);
    assert(array_set_value(roots[2], 1, roots[0], 0, 0) == 1);
    assert(((uintptr_t *)data)[3] == 42);
    assert(array_set_value(roots[2], 1, 0, 0, 0) == 0);
    assert(((uintptr_t *)data)[2] == 0 && ((uintptr_t *)data)[3] == 0);

    roots[3] = allocate_reference_array(1, 4, 1);
    assert(array_set_value(roots[3], 0, roots[0], 0, 0) == 0);
    assert(array_set_value(roots[3], 0, roots[1], 0, 0) == 1);
    assert(array_set_value(roots[3], 0, 0, 0, 0) == 0);

    roots[4] = allocate_value_array(1, 6, 5, 3 * sizeof(uintptr_t));
    uintptr_t nullable_data = *(uintptr_t *)(roots[4] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    assert(array_set_value(roots[4], 0, roots[1], 2, sizeof(uintptr_t)) == 0);
    assert(*(unsigned char *)nullable_data == 1);
    assert(((uintptr_t *)nullable_data)[1] == roots[0]);
    assert(((uintptr_t *)nullable_data)[2] == 42);
    assert(array_set_value(roots[4], 0, roots[0], 2, sizeof(uintptr_t)) == 1);
    assert(((uintptr_t *)nullable_data)[2] == 42);
    uintptr_t child = roots[0];
    roots[0] = roots[1] = 0;
    collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, child));
    assert(array_set_value(roots[4], 0, 0, 2, sizeof(uintptr_t)) == 0);
    for (size_t i = 0; i < 3 * sizeof(uintptr_t); i++) assert(((unsigned char *)nullable_data)[i] == 0);
    collector_collect();
    assert(!netwasm_gc_contains(netwasm_gc_runtime, child));
    return 0;
}
