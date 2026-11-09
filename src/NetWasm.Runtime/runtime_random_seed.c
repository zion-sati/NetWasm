#include <stdint.h>

#include "wasi_random_import.h"

/* The seed for randomized hashing, from wasi:random; a program that never seeds a hash leaves
 * it, and its import, out. A plain core module links runtime_platform_nowasi.c's instead. */
#if !defined(NETWASM_NO_RANDOM_SEED)
#ifdef NETWASM_RUNTIME_PACK
uint64_t random_seed(void)
#else
__attribute__((export_name("random_seed"))) uint64_t random_seed(void)
#endif
{
    return runtime_get_random_u64();
}
#endif
