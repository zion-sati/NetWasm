/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "generational_gc.h"
#include "runtime_state.h"
#include "../../runtime_standard_output.h"

#include <stdlib.h>

static size_t append_text(char *line, size_t at, const char *text)
{
    while (*text != 0) line[at++] = *text++;
    return at;
}

static size_t append_number(char *line, size_t at, uint64_t value)
{
    char digits[20];
    size_t count = 0;
    do {
        digits[count++] = (char)('0' + value % 10);
        value /= 10;
    } while (value != 0);
    while (count != 0) line[at++] = digits[--count];
    return at;
}

/* Qualification: names a store that the compiler or the runtime left without its write barrier, by the type
 * ids of the object stored into and of the object stored and by the slot's offset, and stops the program. */
void collector_missed_reference_write(const void *holder, const void *slot, uintptr_t reference)
{
    char line[192];
    uintptr_t stored = netwasm_gc_base(netwasm_gc_runtime, reference);
    size_t at = append_text(line, 0, "NetWasmGC: a reference was stored without its write barrier: into type ");
    at = append_number(line, at, *(const uint32_t *)holder);
    at = append_text(line, at, " at offset ");
    at = append_number(line, at, (uint64_t)((uintptr_t)slot - (uintptr_t)holder));
    at = append_text(line, at, ", an object of type ");
    at = append_number(line, at, stored == 0 ? 0 : *(const uint32_t *)stored);
    at = append_text(line, at, "\n");
    runtime_write_standard_output(2, line, at);
    abort();
}
