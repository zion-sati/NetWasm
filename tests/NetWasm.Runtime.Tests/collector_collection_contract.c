#include "collector/collector_collection.h"

#include <assert.h>
#include <stdint.h>

static unsigned collection_count;

void GC_gcollect(void) { collection_count++; }
unsigned GC_get_gc_no(void) { return collection_count; }
uint64_t GC_get_total_bytes(void) { return 101; }
uint64_t GC_get_heap_size(void) { return 80; }
uint64_t GC_get_free_bytes(void) { return 30; }

int main(void)
{
    uint64_t pause_milliseconds = 17;

    collector_collect();
    assert(collector_collection_count() == 1);
    assert(collector_total_allocated_bytes() == 101);
    assert(collector_heap_size() == 80);
    assert(collector_free_bytes() == 30);
    assert(!collector_try_read_total_pause_milliseconds(&pause_milliseconds));
    assert(pause_milliseconds == 17);
    return 0;
}
