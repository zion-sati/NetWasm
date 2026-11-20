// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

#ifndef NETWASM_UNMANAGED_ALLOCATOR_LIBC_H
#define NETWASM_UNMANAGED_ALLOCATOR_LIBC_H
#include <stdint.h>
#include <stdlib.h>
#if defined(__APPLE__)
#include <malloc/malloc.h>
#else
#include <malloc.h>
#endif
#include "runtime_libc_initializer.h"

/* Explicitly owned native storage never participates in managed reachability
 * and never invokes a managed collection, including before runtime startup. */
static inline void *netwasm_unmanaged_allocate(size_t size)
{
    runtime_initialize_libc();
    return malloc(size);
}
static inline void netwasm_unmanaged_free(void *address) { free(address); }
static inline void *netwasm_unmanaged_reallocate(void *address, size_t size)
{
    runtime_initialize_libc();
    return realloc(address, size);
}
/* The bytes a block can hold, as upstream's System.Native reads them to copy a reallocated aligned block. */
static inline size_t netwasm_unmanaged_usable_size(void *address)
{
#if defined(__APPLE__)
    return malloc_size(address);
#else
    return malloc_usable_size(address);
#endif
}
/* C11's aligned_alloc, over a size rounded up to the alignment as C11 requires; upstream uses posix_memalign,
 * which -std=c11 does not declare. Freed with free, as upstream's AlignedFree is on Unix. */
static inline void *netwasm_unmanaged_allocate_aligned(size_t alignment, size_t size)
{
    if (size > SIZE_MAX - (alignment - 1)) {
        return NULL;
    }
    runtime_initialize_libc();
    return aligned_alloc(alignment, (size + alignment - 1) & ~(alignment - 1));
}
#endif
