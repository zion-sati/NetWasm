// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include <assert.h>
#include "../../src/NetWasm.Runtime/collector/bump/bump_arena.c"

int main(void)
{
    netwasm_bump_initialize();
    assert(netwasm_bump_heap_size() == 0);
    unsigned char *first = netwasm_bump_allocate(16);
    assert(first != NULL && netwasm_bump_heap_size() == 256 * 1024);
    for (size_t index = 0; index < 16; index++) assert(first[index] == 0);
    assert(netwasm_bump_allocate(256 * 1024 - 16) != NULL);
    assert(netwasm_bump_heap_size() == 256 * 1024);
    assert(netwasm_bump_allocate(16) != NULL);
    assert(netwasm_bump_heap_size() == (256 + 512) * 1024);
    return 0;
}
