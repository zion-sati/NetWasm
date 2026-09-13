#include <stdlib.h>
#include "runtime_process_exit.h"

_Noreturn void _Exit(int status)
{
    runtime_exit_process(status);
}
