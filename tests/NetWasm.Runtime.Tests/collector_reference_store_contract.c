#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "collector/collector_reference_store.h"

int main(void)
{
    netwasm_reference_t slot = 0;
    collector_store_reference(17, &slot, 41);
    if (slot != 41) {
        abort();
    }
    collector_store_reference(17, &slot, 0);
    if (slot != 0) {
        abort();
    }
    uint32_t values[] = {1, 2, 3, 4};
    collector_move_value_range(
        17,
        &values[1],
        &values[0],
        3,
        sizeof(values[0]),
        0);
    const uint32_t moved[] = {1, 1, 2, 3};
    if (memcmp(values, moved, sizeof(values)) != 0) {
        abort();
    }
    collector_clear_value_range(
        17,
        &values[1],
        2,
        sizeof(values[0]),
        0);
    const uint32_t cleared[] = {1, 0, 0, 3};
    if (memcmp(values, cleared, sizeof(values)) != 0) {
        abort();
    }
    puts("Collector reference-store contract PASS");
    return 0;
}
