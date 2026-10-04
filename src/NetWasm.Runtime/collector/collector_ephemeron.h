#ifndef NETWASM_COLLECTOR_EPHEMERON_H
#define NETWASM_COLLECTOR_EPHEMERON_H

#include <stdint.h>

int collector_register_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot);
void collector_unregister_ephemeron(uintptr_t *key_slot, uintptr_t *value_slot);

#endif
