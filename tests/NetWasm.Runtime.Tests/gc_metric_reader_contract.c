#include "gc_metric_reader.h"

#include <assert.h>
#include <stdint.h>
#include <string.h>

static int underflow;
static int pause_supported;

uint64_t collector_collection_count(void) { return 7; }
uint64_t collector_total_allocated_bytes(void) { return 101; }
uint64_t collector_heap_size(void) { return underflow ? 20 : 80; }
uint64_t collector_free_bytes(void) { return 30; }
bool collector_try_read_total_pause_milliseconds(uint64_t *value)
{
    if (!pause_supported) return false;
    *value = 13;
    return true;
}
uint64_t collector_pinned_reference_count(void) { return 3; }

int main(int argc, char **argv)
{
    if (argc > 1) {
        if (strcmp(argv[1], "underflow") == 0) {
            underflow = 1;
            assert(gc_metric_read(3) == 0);
            return 0;
        }
        if (strcmp(argv[1], "pause") == 0) {
            pause_supported = 1;
            assert(gc_metric_supports(4));
            assert(gc_metric_read(4) == 13);
            return 0;
        }
        if (strcmp(argv[1], "invalid-support") == 0) {
            (void)gc_metric_supports(6);
            return 0;
        }
        (void)gc_metric_read(6);
        return 0;
    }

    assert(gc_metric_read(0) == 7);
    assert(gc_metric_supports(0));
    assert(gc_metric_read(1) == 101);
    assert(gc_metric_supports(1));
    assert(gc_metric_read(2) == 80);
    assert(gc_metric_supports(2));
    assert(gc_metric_read(3) == 50);
    assert(gc_metric_supports(3));
    assert(!gc_metric_supports(4));
    assert(gc_metric_read(5) == 3);
    assert(gc_metric_supports(5));
    return 0;
}
