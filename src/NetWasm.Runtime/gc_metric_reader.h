#ifndef NETWASM_GC_METRIC_READER_H
#define NETWASM_GC_METRIC_READER_H

#include <stdbool.h>
#include <stdint.h>

bool gc_metric_supports(uint32_t metric);
uint64_t gc_metric_read(uint32_t metric);

#endif
