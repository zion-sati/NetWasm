#include "collector_collection.h"

#include <gc.h>

void collector_collect(void)
{
    GC_gcollect();
}

uint64_t collector_collection_count(void)
{
    return GC_get_gc_no();
}

uint64_t collector_total_allocated_bytes(void)
{
    return GC_get_total_bytes();
}

uint64_t collector_heap_size(void)
{
    return GC_get_heap_size();
}

uint64_t collector_free_bytes(void)
{
    return GC_get_free_bytes();
}

bool collector_try_read_total_pause_milliseconds(uint64_t *value)
{
    (void)value;
    return false;
}
