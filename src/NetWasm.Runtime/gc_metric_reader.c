#include "gc_metric_reader.h"

#include "collector/collector_collection.h"
#include "collector/collector_pinning.h"

#include <stdlib.h>

typedef bool (*gc_metric_reader)(uint64_t *value);

static bool read_collection_count(uint64_t *value)
{
    *value = collector_collection_count();
    return true;
}

static bool read_total_allocated_bytes(uint64_t *value)
{
    *value = collector_total_allocated_bytes();
    return true;
}

static bool read_heap_size(uint64_t *value)
{
    *value = collector_heap_size();
    return true;
}

static bool read_live_heap_size(uint64_t *value)
{
    uint64_t heap_size = collector_heap_size();
    uint64_t free_bytes = collector_free_bytes();
    *value = free_bytes > heap_size ? 0 : heap_size - free_bytes;
    return true;
}

static bool read_total_pause_milliseconds(uint64_t *value)
{
    return collector_try_read_total_pause_milliseconds(value);
}

static bool read_pinned_reference_count(uint64_t *value)
{
    *value = collector_pinned_reference_count();
    return true;
}

static const gc_metric_reader readers[] = {
    read_collection_count,
    read_total_allocated_bytes,
    read_heap_size,
    read_live_heap_size,
    read_total_pause_milliseconds,
    read_pinned_reference_count,
};

bool gc_metric_supports(uint32_t metric)
{
    if (metric >= sizeof(readers) / sizeof(readers[0])) abort();
    uint64_t value;
    return readers[metric](&value);
}

uint64_t gc_metric_read(uint32_t metric)
{
    if (metric >= sizeof(readers) / sizeof(readers[0])) abort();
    uint64_t value;
    if (!readers[metric](&value)) abort();
    return value;
}
