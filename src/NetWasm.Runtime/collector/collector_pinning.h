#ifndef NETWASM_COLLECTOR_PINNING_H
#define NETWASM_COLLECTOR_PINNING_H

#include <stdint.h>

void collector_pin_reference(uintptr_t *slot);
void collector_unpin_reference(uintptr_t *slot);
uint64_t collector_pinned_reference_count(void);

#endif
