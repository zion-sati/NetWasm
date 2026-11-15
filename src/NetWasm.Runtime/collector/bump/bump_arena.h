// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_BUMP_ARENA_H
#define NETWASM_BUMP_ARENA_H

#include <stddef.h>
#include <stdint.h>

void netwasm_bump_initialize(void);
void *netwasm_bump_allocate(size_t size);
uint64_t netwasm_bump_total_allocated_bytes(void);
uint64_t netwasm_bump_heap_size(void);
uint64_t netwasm_bump_free_bytes(void);

#endif
