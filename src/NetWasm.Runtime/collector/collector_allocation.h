#ifndef NETWASM_COLLECTOR_ALLOCATION_H
#define NETWASM_COLLECTOR_ALLOCATION_H

#include <stddef.h>
#include <stdint.h>

typedef uintptr_t NetWasmCollectorDescriptor;
typedef uintptr_t NetWasmCollectorDescriptorWord;

void collector_mark_descriptor_word(NetWasmCollectorDescriptorWord *bitmap, size_t bit);
NetWasmCollectorDescriptor collector_create_descriptor(
    const NetWasmCollectorDescriptorWord *bitmap,
    size_t length);
void *collector_allocate_exact(size_t size, NetWasmCollectorDescriptor descriptor);
void *collector_allocate_exact_zeroed(
    size_t count,
    size_t size,
    NetWasmCollectorDescriptor descriptor);
void *collector_allocate_atomic(size_t size);

#endif
