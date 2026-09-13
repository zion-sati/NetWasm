#include "runtime_system_initializer.h"
#include "runtime_environment_reader.h"
#include "runtime_libc_initializer.h"

/* Own libc's environment storage so linking getenv does not pull in a legacy
 * toolchain environment constructor. Preserve libc's ordinary symbol aliases. */
char **__environ;
#ifdef __wasm__
extern char **environ __attribute__((weak, alias("__environ")));
extern char **_environ __attribute__((weak, alias("__environ")));
extern char **___environ __attribute__((weak, alias("__environ")));
#endif

void runtime_initialize_system(void) {
    runtime_initialize_libc();
    __environ = runtime_read_environment();
}
