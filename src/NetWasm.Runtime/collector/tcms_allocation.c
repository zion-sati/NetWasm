#include "collector_allocation.h"
#include "collector_metadata.h"
#include "tcms_runtime_state.h"
#include <stdlib.h>

typedef struct { size_t count; size_t offsets[]; } Descriptor;

void collector_mark_descriptor_word(NetWasmCollectorDescriptorWord *bitmap, size_t bit)
{
    size_t bits = sizeof(uintptr_t) * 8;
    bitmap[bit / bits] |= (uintptr_t)1 << (bit % bits);
}
NetWasmCollectorDescriptor collector_create_descriptor(
    const NetWasmCollectorDescriptorWord *bitmap, size_t length)
{
    if ((length != 0 && bitmap == NULL) || length > SIZE_MAX / sizeof(uintptr_t)) abort();
    size_t bits = sizeof(uintptr_t) * 8, count = 0;
    for (size_t bit = 0; bit < length; bit++)
        count += (bitmap[bit / bits] >> (bit % bits)) & 1;
    if (count == 0) return 0;
    if (count > (SIZE_MAX - sizeof(Descriptor)) / sizeof(size_t)) abort();
    Descriptor *descriptor = collector_allocate_metadata(sizeof(*descriptor) + count * sizeof(size_t));
    /* Never substitute an atomic or conservative layout when metadata fails. */
    if (descriptor == NULL) abort();
    descriptor->count = count;
    size_t index = 0;
    for (size_t bit = 0; bit < length; bit++)
        if ((bitmap[bit / bits] >> (bit % bits)) & 1) descriptor->offsets[index++] = bit * sizeof(uintptr_t);
    return (uintptr_t)descriptor;
}
static void *allocate(size_t count, size_t size, NetWasmCollectorDescriptor token)
{
    if (size != 0 && count > SIZE_MAX / size) return NULL;
    Descriptor *descriptor = (Descriptor *)token;
    TcmsLayout layout = descriptor == NULL ? (TcmsLayout){0} :
        (TcmsLayout){descriptor->offsets, descriptor->count, size, count};
    void *object = NULL;
    TcmsResult status = tcms_allocate(&netwasm_tcms, count * size, layout, NULL, 0, &object);
    if (status == TCMS_INVALID_LAYOUT || status == TCMS_INVALID_ARGUMENT) abort();
    if (status != TCMS_OK) return NULL;
    netwasm_tcms_allocated_bytes += (uint64_t)count * size;
    return object;
}
void *collector_allocate_exact(size_t size, NetWasmCollectorDescriptor descriptor)
{
    return allocate(1, size, descriptor);
}
void *collector_allocate_exact_zeroed(size_t count, size_t size, NetWasmCollectorDescriptor descriptor)
{
    return allocate(count, size, descriptor);
}
void *collector_allocate_atomic(size_t size) { return allocate(1, size, 0); }
