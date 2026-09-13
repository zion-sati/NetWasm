#include <stdint.h>
#include <stdlib.h>
#include "runtime_process_exit.h"
#include "wasi_import_attributes.h"

NETWASM_WASI_IMPORT("wasi:cli/exit@0.2", "exit")
void runtime_wasi_exit(uint32_t is_error);

_Noreturn void runtime_exit_process(int status)
{
    runtime_wasi_exit(status != 0);
    /* A returning host violates the exit contract; never resume the caller. */
    abort();
}
