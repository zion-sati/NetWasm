#ifndef NETWASM_COLLECTOR_COLLECTION_H
#define NETWASM_COLLECTOR_COLLECTION_H

#include <stdbool.h>
#include <stdint.h>

void collector_collect(void);
uint64_t collector_collection_count(void);
uint64_t collector_total_allocated_bytes(void);
uint64_t collector_heap_size(void);
uint64_t collector_free_bytes(void);
bool collector_try_read_total_pause_milliseconds(uint64_t *value);

#endif
