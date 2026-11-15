// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT
#include <assert.h>
#ifndef NULLABLE_READBACK_TEST_HOOKS
#define NETWASM_RUNTIME_PACK
#endif
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wattributes"
#include "../../src/NetWasm.Runtime/native_runtime.c"
#pragma GCC diagnostic pop
#include "../../src/NetWasm.Runtime/collector/netwasm/runtime_state.h"
void runtime_initialize_libc(void) { }
void runtime_initialize_system(void) { }
u32 netwasm_managed_finalize(netwasm_reference_t object, u32 type) { (void)object; (void)type; return 0; }
static void mark_word(uintptr_t *bitmap, size_t bit)
{
    *bitmap |= (uintptr_t)1 << bit;
}
int main(void)
{
    initialize(0, 8, 8);
    uintptr_t box_bits = 0, value_bits = 0, nullable_bits = 0, array_bits = 0;
    mark_word(&box_bits, 1);
    mark_word(&value_bits, 0);
    mark_word(&nullable_bits, 1);
    mark_word(&array_bits, NETWASM_ARRAY_DATA_POINTER_OFFSET / sizeof(uintptr_t));
    register_type(1, 0, 2 * sizeof(uintptr_t), (uintptr_t)&box_bits, 2, 0, 0, 0, 0);
    register_value_type(1, sizeof(uintptr_t), sizeof(uintptr_t), (uintptr_t)&value_bits, 1);
    register_type(2, 0, 3 * sizeof(uintptr_t), 0, 0, 0, 0, 0, 0);
    register_value_type(2, 2 * sizeof(uintptr_t), sizeof(uintptr_t), (uintptr_t)&nullable_bits, 2);
    register_type(3, 0, NETWASM_ARRAY_OBJECT_SIZE, (uintptr_t)&array_bits,
        NETWASM_ARRAY_OBJECT_SIZE / sizeof(uintptr_t), 0, 0, 0, 0);
    register_type(4, 0, sizeof(uintptr_t), 0, 0, 0, 0, 0, 0);
    uintptr_t roots[3] = {0}; collector_register_roots(roots, sizeof(roots));
    roots[0] = allocate_value_array(2, 3, 2, 2 * sizeof(uintptr_t));
    uintptr_t data = *(uintptr_t *)(roots[0] + NETWASM_ARRAY_DATA_POINTER_OFFSET);
    uintptr_t child = allocate(sizeof(uintptr_t), 4);
    ((unsigned char *)data)[2 * sizeof(uintptr_t)] = 1;
    collector_store_reference(roots[0], (uintptr_t *)(data + 3 * sizeof(uintptr_t)), child);
    assert(array_get_value(roots[0], 0, 1, sizeof(uintptr_t)) == 0);
#ifdef NULLABLE_READBACK_TEST_HOOKS
    collect_before_allocation = 1;
#endif
    roots[1] = array_get_value(roots[0], 1, 1, sizeof(uintptr_t));
    assert(roots[1] != 0 && roots[1] != 1 && *(u32 *)roots[1] == 1);
    assert(*(uintptr_t *)(roots[1] + sizeof(uintptr_t)) == child);
    roots[0] = 0; collector_collect();
    assert(netwasm_gc_contains(netwasm_gc_runtime, child));
    assert(*(uintptr_t *)(roots[1] + sizeof(uintptr_t)) == child);
    roots[2] = allocate_reference_array(1, 3, 4);
    assert(array_get_value(roots[2], 0, 0, 0) == 0);
    return 0;
}
