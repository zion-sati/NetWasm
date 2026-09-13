#include <assert.h>
#include <setjmp.h>
#include <stdlib.h>
#include "runtime_process_exit.h"

static jmp_buf continuation;
static int observed;

_Noreturn void runtime_exit_process(int status)
{
    observed = status;
    longjmp(continuation, 1);
}

int main(void)
{
    if (setjmp(continuation) == 0) { _Exit(73); }
    assert(observed == 73);
    return 0;
}
