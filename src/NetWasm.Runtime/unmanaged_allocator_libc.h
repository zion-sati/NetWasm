#ifndef NETWASM_UNMANAGED_ALLOCATOR_LIBC_H
#define NETWASM_UNMANAGED_ALLOCATOR_LIBC_H
#include <stdlib.h>
#include "runtime_libc_initializer.h"

/* Explicitly owned native storage never participates in managed reachability
 * and never invokes a managed collection, including before runtime startup. */
static inline void *netwasm_unmanaged_allocate(size_t size)
{
    runtime_initialize_libc();
    return malloc(size);
}
static inline void netwasm_unmanaged_free(void *address) { free(address); }
#endif
