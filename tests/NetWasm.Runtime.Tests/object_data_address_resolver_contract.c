#include "object_data_address_resolver.h"

#include <assert.h>
#include <stdint.h>
#include <string.h>

int main(int argc, char **argv)
{
    uint8_t data_kinds[] = {
        NETWASM_OBJECT_DATA_FIELDS,
        NETWASM_OBJECT_DATA_STRING,
        NETWASM_OBJECT_DATA_ARRAY,
    };
    uint8_t storage[64] = {0};
    netwasm_reference_t target = (netwasm_reference_t)(uintptr_t)storage;

    if (argc > 1) {
        (void)argv;
        *(uint32_t *)storage = 3;
        (void)object_data_address_resolve(target, 3, data_kinds, 8, 12, 16);
        return 0;
    }

    assert(object_data_address_resolve(0, 3, data_kinds, 8, 12, 16) == 0);
    *(uint32_t *)storage = 0;
    assert(object_data_address_resolve(target, 3, data_kinds, 8, 12, 16) == target + 8);
    *(uint32_t *)storage = 1;
    assert(object_data_address_resolve(target, 3, data_kinds, 8, 12, 16) == target + 12);
    *(uint32_t *)storage = 2;
    netwasm_address_t array_data = target + 40;
    memcpy(storage + 16, &array_data, sizeof(array_data));
    assert(object_data_address_resolve(target, 3, data_kinds, 8, 12, 16) == array_data);
    return 0;
}
