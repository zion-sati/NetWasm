/* NetWasmGC for NetWasm (https://www.netwasm.com)
 * Copyright (c) 2026 Zion Sati
 * SPDX-License-Identifier: Apache-2.0
 */
#include "../collector_collection.h"
#include "runtime_state.h"

#ifdef NETWASM_GC_STATISTICS
#include "../../runtime_standard_output.h"

#include <inttypes.h>
#include <stdio.h>

/* A diagnostic build reports its cumulative statistics on stderr whenever the
 * allocated-bytes counter is read. Workloads read it on both sides of their
 * timed region, so the difference between two reports covers that region. */
static void write_statistics(void)
{
    NetWasmGcStatistics statistics;
    netwasm_gc_read_statistics(netwasm_gc_runtime, &statistics);
    char line[2048];
    size_t length = (size_t)snprintf(line, sizeof line, "NWGC-STATS");
#define WRITE_FIELD(name) \
    if (length < sizeof line) \
        length += (size_t)snprintf(line + length, sizeof line - length, \
            "|" #name "=%" PRIu64, statistics.name);
    NETWASM_GC_STATISTICS_FIELDS(WRITE_FIELD)
#undef WRITE_FIELD
    if (length < sizeof line - 1) line[length++] = '\n';
    runtime_write_standard_output(2, line, length < sizeof line ? length : sizeof line - 1);
}
#endif

void collector_collect(void)
{
    netwasm_gc_collect(netwasm_gc_runtime);
}

void collector_collect_young(void)
{
    netwasm_gc_collect_young(netwasm_gc_runtime);
}

bool collector_collect_when_idle(void)
{
    return netwasm_gc_collect_when_idle(netwasm_gc_runtime);
}

unsigned collector_collect_when_quiet(void)
{
    return netwasm_gc_collect_when_quiet(netwasm_gc_runtime);
}

uint64_t collector_collection_count(void)
{
    return netwasm_gc_collection_count(netwasm_gc_runtime);
}

uint64_t collector_oldest_generation_collection_count(void)
{
    return netwasm_gc_oldest_generation_collection_count(netwasm_gc_runtime);
}

uint32_t collector_object_generation(uintptr_t object)
{
    return netwasm_gc_object_generation(netwasm_gc_runtime, object);
}

uint64_t collector_total_allocated_bytes(void)
{
#ifdef NETWASM_GC_STATISTICS
    write_statistics();
#endif
    return netwasm_gc_total_allocated_bytes(netwasm_gc_runtime);
}

uint64_t collector_heap_size(void)
{
    return netwasm_gc_live_bytes(netwasm_gc_runtime);
}

uint64_t collector_free_bytes(void)
{
    return 0;
}

bool collector_try_read_total_pause_milliseconds(uint64_t *value)
{
    (void)value;
    return false;
}
