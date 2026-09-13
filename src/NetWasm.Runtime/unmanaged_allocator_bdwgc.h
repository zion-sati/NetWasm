#ifndef NETWASM_UNMANAGED_ALLOCATOR_BDWGC_H
#define NETWASM_UNMANAGED_ALLOCATOR_BDWGC_H

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

#include "gc.h"
#include "runtime_libc_initializer.h"

/* Canonical lowering can allocate before GC_INIT reads the environment.
 * Retain the owner because those buffers may be freed after GC_INIT. */
typedef union {
    struct {
        void *allocation;
        int collector_owned;
    } owner;
    max_align_t alignment;
} NetWasmUnmanagedAllocationHeader;

static inline void *netwasm_unmanaged_allocate(size_t size)
{
    size_t alignment = _Alignof(max_align_t);
    size_t overhead = sizeof(NetWasmUnmanagedAllocationHeader) + alignment - 1;
    if (size > SIZE_MAX - overhead) {
        return NULL;
    }
    runtime_initialize_libc();
    int collector_owned = GC_is_init_called();
    size_t allocation_size = size + overhead;
    void *allocation = collector_owned
        ? GC_malloc_atomic_uncollectable(allocation_size)
        : malloc(allocation_size);
    if (allocation == NULL) {
        return NULL;
    }
    uintptr_t address = ((uintptr_t)allocation + overhead) &
        ~(uintptr_t)(alignment - 1);
    NetWasmUnmanagedAllocationHeader *header =
        (NetWasmUnmanagedAllocationHeader *)address - 1;
    header->owner.allocation = allocation;
    header->owner.collector_owned = collector_owned;
    return header + 1;
}

static inline void netwasm_unmanaged_free(void *allocation)
{
    if (allocation == NULL) {
        return;
    }
    NetWasmUnmanagedAllocationHeader *header =
        (NetWasmUnmanagedAllocationHeader *)allocation - 1;
    if (header->owner.collector_owned) {
        GC_free(header->owner.allocation);
    } else {
        free(header->owner.allocation);
    }
}

#endif
