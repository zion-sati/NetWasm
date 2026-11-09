#ifndef NETWASM_WASI_RANDOM_IMPORT_H
#define NETWASM_WASI_RANDOM_IMPORT_H

#include <stdint.h>
#include "wasi_import_attributes.h"

NETWASM_WASI_IMPORT("wasi:random/random@0.3", "get-random-u64")
uint64_t runtime_get_random_u64(void);

#endif
