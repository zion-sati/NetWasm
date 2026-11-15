// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <assert.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

static size_t backing_allocations;

static void *test_calloc(size_t count, size_t size)
{
    backing_allocations++;
    return calloc(count, size);
}

#define NETWASM_BUMP_INITIAL_CHUNK_BYTES 64
#define NETWASM_BUMP_MAX_CHUNK_BYTES 256
#define calloc test_calloc
#include "../../src/NetWasm.Runtime/collector/bump/bump_arena.c"
#undef calloc

static void assert_zeroed(const unsigned char *bytes, size_t length)
{
    for (size_t index = 0; index < length; index++) assert(bytes[index] == 0);
}

int main(void)
{
    netwasm_bump_initialize();
    assert(backing_allocations == 0);
    assert(netwasm_bump_total_allocated_bytes() == 0);
    assert(netwasm_bump_heap_size() == 0);
    assert(netwasm_bump_free_bytes() == 0);

    unsigned char *first = netwasm_bump_allocate(1);
    unsigned char *second = netwasm_bump_allocate(16);
    assert(first != NULL && second != NULL && first != second);
    assert((uintptr_t)first % 16 == 0 && (uintptr_t)second % 16 == 0);
    assert(backing_allocations == 1);
    assert_zeroed(first, 1);
    assert_zeroed(second, 16);
    first[0] = 0x5a;
    second[15] = 0xa5;

    assert(netwasm_bump_allocate(32) != NULL);
    assert(backing_allocations == 1);
    assert(netwasm_bump_allocate(1) != NULL);
    assert(backing_allocations == 2);

    unsigned char *large = netwasm_bump_allocate(200);
    assert(large != NULL && (uintptr_t)large % 16 == 0);
    assert_zeroed(large, 200);
    assert(backing_allocations == 3);
    assert(netwasm_bump_allocate(16) != NULL);
    assert(backing_allocations == 3);

    assert(netwasm_bump_total_allocated_bytes() == 266);
    assert(netwasm_bump_heap_size() == 400);
    assert(netwasm_bump_free_bytes() == 96);
    assert(netwasm_bump_allocate(SIZE_MAX) == NULL);
    assert(backing_allocations == 3);
    return 0;
}
