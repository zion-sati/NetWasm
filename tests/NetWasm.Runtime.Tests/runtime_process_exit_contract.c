#include <assert.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>

#include "runtime_process_exit.h"

static jmp_buf continuation;
static uint32_t observed_status;
static unsigned exit_calls;
static unsigned exit_with_code_calls;
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

void runtime_wasi_exit_with_code(uint32_t status_code)
{
    observed_status = status_code;
    ++exit_with_code_calls;
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
    /* Success exits with exit's ok; any other status keeps its low eight bits. */
    if (setjmp(continuation) == 0) { runtime_exit_process(0); }
    assert(exit_calls == 1 && exit_with_code_calls == 0 && observed_status == 0);
    if (setjmp(continuation) == 0) { runtime_exit_process(7); }
    assert(exit_calls == 1 && exit_with_code_calls == 1 && observed_status == 7);
    if (setjmp(continuation) == 0) { runtime_exit_process(-1); }
    assert(exit_calls == 1 && exit_with_code_calls == 2 && observed_status == 255);
    if (setjmp(continuation) == 0) { runtime_exit_process(259); }
    assert(exit_calls == 1 && exit_with_code_calls == 3 && observed_status == 3);
    assert(abort_calls == 0);
    /* A host that returns from either import never resumes the caller. */
    host_returns = 1;
    expect_abort = 1;
    int result = setjmp(continuation);
    if (result == 0) { runtime_exit_process(0); }
    assert(result == 2);
    assert(exit_calls == 2 && observed_status == 0 && abort_calls == 1);
    result = setjmp(continuation);
    if (result == 0) { runtime_exit_process(5); }
    expect_abort = 0;
    assert(result == 2);
    assert(exit_with_code_calls == 4 && observed_status == 5 && abort_calls == 2);
    return 0;
}
