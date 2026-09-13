#include "object_data_address_resolver.h"

#include <stdlib.h>

netwasm_address_t object_data_address_resolve(
    netwasm_reference_t target,
    uint32_t type_capacity,
    const uint8_t *type_data_kinds,
    netwasm_address_t object_header_size,
    netwasm_address_t string_data_offset,
    netwasm_address_t array_data_pointer_offset)
{
    if (target == 0) return 0;

    uint32_t type_id = *(uint32_t *)(uintptr_t)target;
    if (type_id >= type_capacity) abort();
    if (type_data_kinds[type_id] == NETWASM_OBJECT_DATA_ARRAY) {
        return *(netwasm_address_t *)(uintptr_t)(target + array_data_pointer_offset);
    }

    return target + (type_data_kinds[type_id] == NETWASM_OBJECT_DATA_STRING
        ? string_data_offset
        : object_header_size);
}
