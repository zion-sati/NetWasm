#include <stdint.h>
#include <stdio.h>

#include "../../src/NetWasm.Runtime/collector/collector_identity_hash.h"

int main(void)
{
    uint32_t first = collector_identity_hash((netwasm_reference_t)16);
    uint32_t second = collector_identity_hash((netwasm_reference_t)32);

    if (collector_identity_hash((netwasm_reference_t)0) != 0 ||
        first != collector_identity_hash((netwasm_reference_t)16) ||
        first == second)
    {
        return 1;
    }

    puts("Collector identity-hash contract PASS");
    return 0;
}
