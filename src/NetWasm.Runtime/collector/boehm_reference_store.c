#include "collector_reference_store.h"
#include <string.h>

void collector_store_reference(
    netwasm_reference_t owner,
    netwasm_reference_t *slot,
    netwasm_reference_t value)
{
    (void)owner;
    *slot = value;
}

void collector_move_value_range(
    netwasm_reference_t owner,
    void *destination,
    const void *source,
    size_t element_count,
    size_t element_size,
    NetWasmCollectorDescriptor descriptor)
{
    (void)owner;
    (void)descriptor;
    memmove(destination, source, element_count * element_size);
}

void collector_clear_value_range(
    netwasm_reference_t owner,
    void *destination,
    size_t element_count,
    size_t element_size,
    NetWasmCollectorDescriptor descriptor)
{
    (void)owner;
    (void)descriptor;
    memset(destination, 0, element_count * element_size);
}
