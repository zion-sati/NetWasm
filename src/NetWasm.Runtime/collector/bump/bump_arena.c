// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#include "bump_arena.h"

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

#ifndef NETWASM_BUMP_INITIAL_CHUNK_BYTES
#define NETWASM_BUMP_INITIAL_CHUNK_BYTES (256 * 1024)
#endif

#ifndef NETWASM_BUMP_MAX_CHUNK_BYTES
#define NETWASM_BUMP_MAX_CHUNK_BYTES (4 * 1024 * 1024)
#endif

#define NETWASM_BUMP_ALIGNMENT 16

typedef struct NetWasmBumpChunk {
    struct NetWasmBumpChunk *next;
    unsigned char *data;
    size_t capacity;
    size_t used;
} NetWasmBumpChunk;

static NetWasmBumpChunk *chunks;
static NetWasmBumpChunk *current;
static size_t next_capacity = NETWASM_BUMP_INITIAL_CHUNK_BYTES;
static uint64_t total_allocated_bytes;
static uint64_t heap_size;
static uint64_t consumed_bytes;

_Static_assert(
    NETWASM_BUMP_INITIAL_CHUNK_BYTES > 0 &&
        NETWASM_BUMP_INITIAL_CHUNK_BYTES <= NETWASM_BUMP_MAX_CHUNK_BYTES,
    "The Bump chunk bounds are invalid");
_Static_assert(
    (NETWASM_BUMP_ALIGNMENT & (NETWASM_BUMP_ALIGNMENT - 1)) == 0,
    "The Bump alignment must be a power of two");
_Static_assert(
    NETWASM_BUMP_ALIGNMENT >= _Alignof(max_align_t),
    "The Bump alignment must satisfy the platform maximum alignment");

static int align_size(size_t size, size_t *aligned)
{
    size_t nonzero = size == 0 ? 1 : size;
    if (nonzero > SIZE_MAX - (NETWASM_BUMP_ALIGNMENT - 1)) return 0;
    *aligned = (nonzero + NETWASM_BUMP_ALIGNMENT - 1) &
        ~(size_t)(NETWASM_BUMP_ALIGNMENT - 1);
    return 1;
}

static NetWasmBumpChunk *allocate_chunk(size_t capacity)
{
    if (capacity > SIZE_MAX - sizeof(NetWasmBumpChunk) -
            (NETWASM_BUMP_ALIGNMENT - 1) ||
        heap_size > UINT64_MAX - capacity) {
        return NULL;
    }

    size_t bytes = sizeof(NetWasmBumpChunk) +
        (NETWASM_BUMP_ALIGNMENT - 1) + capacity;
    NetWasmBumpChunk *chunk = calloc(1, bytes);
    if (chunk == NULL) return NULL;

    uintptr_t payload = (uintptr_t)(chunk + 1);
    payload = (payload + NETWASM_BUMP_ALIGNMENT - 1) &
        ~(uintptr_t)(NETWASM_BUMP_ALIGNMENT - 1);
    chunk->next = chunks;
    chunk->data = (unsigned char *)payload;
    chunk->capacity = capacity;
    chunks = chunk;
    heap_size += capacity;
    return chunk;
}

static void advance_capacity(size_t capacity)
{
    next_capacity = capacity >= NETWASM_BUMP_MAX_CHUNK_BYTES / 2
        ? NETWASM_BUMP_MAX_CHUNK_BYTES
        : capacity * 2;
}

void netwasm_bump_initialize(void)
{
    /* Static zero initialization is the complete collector initialization.
       Chunks remain lazy so applications that allocate nothing retain no arena. */
}

void *netwasm_bump_allocate(size_t size)
{
    size_t aligned;
    if (!align_size(size, &aligned) ||
        total_allocated_bytes > UINT64_MAX - size ||
        consumed_bytes > UINT64_MAX - aligned) {
        return NULL;
    }

    if (current == NULL || aligned > current->capacity - current->used) {
        if (aligned > next_capacity / 2) {
            NetWasmBumpChunk *dedicated = allocate_chunk(aligned);
            if (dedicated == NULL) return NULL;
            dedicated->used = aligned;
            total_allocated_bytes += size;
            consumed_bytes += aligned;
            return dedicated->data;
        }

        current = allocate_chunk(next_capacity);
        if (current == NULL) return NULL;
        advance_capacity(current->capacity);
    }

    void *result = current->data + current->used;
    current->used += aligned;
    total_allocated_bytes += size;
    consumed_bytes += aligned;
    return result;
}

uint64_t netwasm_bump_total_allocated_bytes(void)
{
    return total_allocated_bytes;
}

uint64_t netwasm_bump_heap_size(void)
{
    return heap_size;
}

uint64_t netwasm_bump_free_bytes(void)
{
    return heap_size - consumed_bytes;
}
