#include <stdint.h>
#include <stdlib.h>
#include "runtime_process_exit.h"
#include "wasi_import_attributes.h"

NETWASM_WASI_IMPORT("wasi:cli/exit@0.3", "exit")
void runtime_wasi_exit(uint32_t is_error);

NETWASM_WASI_IMPORT("wasi:cli/exit@0.3", "exit-with-code")
void runtime_wasi_exit_with_code(uint32_t status_code);

/* Success exits with exit's ok; any other status carries its code, which exit's result cannot,
 * truncated to the eight bits a process status has, as on Linux. */
_Noreturn void runtime_exit_process(int status)
{
    if (status == 0) {
        runtime_wasi_exit(0);
    } else {
        runtime_wasi_exit_with_code((uint8_t)status);
    }
    /* A returning host violates the exit contract; never resume the caller. */
    abort();
}
