#include <assert.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>

#include "runtime_process_exit.h"

static jmp_buf continuation;
static uint32_t observed_status;
static unsigned exit_calls;
static unsigned abort_calls;
static int host_returns;
static int expect_abort;

void runtime_wasi_exit(uint32_t is_error)
{
    observed_status = is_error;
    ++exit_calls;
    if (!host_returns) {
        longjmp(continuation, 1);
    }
}

void abort(void)
{
    if (!expect_abort) { _Exit(1); }
    ++abort_calls;
    longjmp(continuation, 2);
}

int main(void)
{
    if (setjmp(continuation) == 0) { runtime_exit_process(0); }
    assert(exit_calls == 1 && observed_status == 0 && abort_calls == 0);
    if (setjmp(continuation) == 0) { runtime_exit_process(7); }
    assert(exit_calls == 2 && observed_status == 1 && abort_calls == 0);
    if (setjmp(continuation) == 0) { runtime_exit_process(-1); }
    assert(exit_calls == 3 && observed_status == 1 && abort_calls == 0);
    host_returns = 1;
    expect_abort = 1;
    int result = setjmp(continuation);
    if (result == 0) { runtime_exit_process(0); }
    expect_abort = 0;
    assert(result == 2);
    assert(exit_calls == 4 && observed_status == 0 && abort_calls == 1);
    return 0;
}
