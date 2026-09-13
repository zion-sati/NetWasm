#include "collector_allocation.h"

#include <gc.h>
#include <gc_mark.h>
#include <gc_typed.h>

void collector_mark_descriptor_word(NetWasmCollectorDescriptorWord *bitmap, size_t bit)
{
    GC_set_bit((GC_word *)bitmap, bit);
}

NetWasmCollectorDescriptor collector_create_descriptor(
    const NetWasmCollectorDescriptorWord *bitmap,
    size_t length)
{
    return GC_make_descriptor((const GC_word *)bitmap, length);
}

void *collector_allocate_exact(size_t size, NetWasmCollectorDescriptor descriptor)
{
    return GC_malloc_explicitly_typed(size, (GC_descr)descriptor);
}

void *collector_allocate_exact_zeroed(
    size_t count,
    size_t size,
    NetWasmCollectorDescriptor descriptor)
{
    return GC_calloc_explicitly_typed(count, size, (GC_descr)descriptor);
}

void *collector_allocate_atomic(size_t size)
{
    return GC_malloc_atomic(size);
}
