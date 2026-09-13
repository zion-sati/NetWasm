#include "runtime_libc_initializer.h"

extern void __wasm_call_ctors(void);
static int initialized;

void runtime_initialize_libc(void)
{
    if (initialized) {
        return;
    }
    __wasm_call_ctors();
    initialized = 1;
}
