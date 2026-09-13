#include <assert.h>
#include "runtime_libc_initializer.h"

static unsigned constructor_calls;

void __wasm_call_ctors(void)
{
    ++constructor_calls;
}

int main(void)
{
    assert(constructor_calls == 0);
    runtime_initialize_libc();
    assert(constructor_calls == 1);
    runtime_initialize_libc();
    assert(constructor_calls == 1);
    return 0;
}
