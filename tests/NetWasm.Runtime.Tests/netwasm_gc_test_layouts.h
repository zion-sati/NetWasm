// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT
#ifndef NETWASM_GC_TEST_LAYOUTS_H
#define NETWASM_GC_TEST_LAYOUTS_H

#include "../../src/NetWasm.Runtime/collector/netwasm/netwasm_gc.h"

#include <assert.h>
#include <stdint.h>
#include <stdlib.h>

/* Objects carry no collector header, so these contracts remember each
 * object's layout by address, the way the runtime reads it from a type id.
 * An object is count elements of size bytes with references at the
 * descriptor's offsets. */
typedef struct {
    const size_t *offsets;
    size_t reference_count;
} NetWasmGcDescriptor;

typedef struct {
    uintptr_t object;
    size_t count;
    size_t size;
    const NetWasmGcDescriptor *descriptor;
} TestLayout;

#define TEST_LAYOUT_CAPACITY ((size_t)1 << 18)
static TestLayout test_layouts[TEST_LAYOUT_CAPACITY];

static inline TestLayout *test_layout_entry(uintptr_t object)
{
    size_t index = (size_t)((object >> 4) * 0x9e3779b97f4a7c15u) & (TEST_LAYOUT_CAPACITY - 1);
    while (test_layouts[index].object != 0 && test_layouts[index].object != object)
        index = (index + 1) & (TEST_LAYOUT_CAPACITY - 1);
    return &test_layouts[index];
}

/* The core calls this as it would the runtime's. */
void collector_object_layout(const void *object, NetWasmCollectorLayout *layout)
{
    const TestLayout *entry = test_layout_entry((uintptr_t)object);
    assert(entry->object == (uintptr_t)object);
    const NetWasmGcDescriptor *descriptor = entry->descriptor;
    *layout = (NetWasmCollectorLayout){
        .element_offsets = descriptor == NULL ? NULL : descriptor->offsets,
        .element_reference_count = descriptor == NULL ? 0 : descriptor->reference_count,
        .element_size = entry->size,
        .element_count = entry->count,
    };
}

static inline bool test_contains_references(size_t count, const NetWasmGcDescriptor *descriptor)
{
    return descriptor != NULL && descriptor->reference_count != 0 && count != 0;
}

static inline void test_record(void *object, size_t count, size_t size, const NetWasmGcDescriptor *descriptor)
{
    TestLayout *entry = test_layout_entry((uintptr_t)object);
    *entry = (TestLayout){(uintptr_t)object, count, size, descriptor};
}

static inline NetWasmGcResult test_allocate(
    NetWasmGc *gc,
    size_t count,
    size_t size,
    const NetWasmGcDescriptor *descriptor,
    void **result)
{
    if (size != 0 && count > SIZE_MAX / size) return NETWASM_GC_SIZE_OVERFLOW;
    NetWasmGcResult status = netwasm_gc_allocate(
        gc, count * size, test_contains_references(count, descriptor), result);
    if (status == NETWASM_GC_OK) test_record(*result, count, size, descriptor);
    return status;
}

#ifndef NETWASM_GC_COMPACT
static inline void *test_try_allocate_available(
    NetWasmGc *gc,
    size_t count,
    size_t size,
    const NetWasmGcDescriptor *descriptor)
{
    if (size != 0 && count > SIZE_MAX / size) return NULL;
    void *result = netwasm_gc_try_allocate_available(
        gc, count * size, test_contains_references(count, descriptor));
    if (result != NULL) test_record(result, count, size, descriptor);
    return result;
}
#endif

#endif
