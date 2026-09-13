#include <assert.h>
#include <errno.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "runtime_standard_output.h"
#include "wasi_output_imports.h"

static unsigned acquisitions[2];
static unsigned drops[2];
static unsigned writes;
static unsigned error_drops;
static unsigned fail_on_write;
static uint8_t failure_tag = 1;
static uint8_t failure_kind;
static unsigned char observed[16384];
static size_t observed_length;
static int expect_abort;
static jmp_buf abort_target;

uint32_t runtime_get_wasi_stdout(void) { ++acquisitions[0]; return 10; }
uint32_t runtime_get_wasi_stderr(void) { ++acquisitions[1]; return 11; }

void runtime_drop_wasi_output(uint32_t handle)
{
    assert(handle == 10 || handle == 11);
    ++drops[handle - 10];
}

void runtime_drop_wasi_output_error(uint32_t handle)
{
    assert(handle == 99);
    ++error_drops;
}

void runtime_write_wasi_output(uint32_t handle, const void *contents,
                              size_t length, NetWasmWasiOutputResult *result)
{
    assert(handle == 10 || handle == 11);
    assert(length > 0 && length <= 4096);
    ++writes;
    if (writes == fail_on_write) {
        result->is_error = failure_tag;
        result->error_kind = failure_kind;
        result->error_handle = 99;
        return;
    }
    assert(observed_length + length <= sizeof(observed));
    memcpy(observed + observed_length, contents, length);
    observed_length += length;
    /* Padding is not part of either discriminant's canonical representation. */
    memset(result, 0xa5, sizeof(*result));
    result->is_error = 0;
}

void abort(void)
{
    if (!expect_abort) { _Exit(1); }
    longjmp(abort_target, 1);
}

int main(int argc, char **argv)
{
    assert(argc == 2);
    unsigned char contents[8193];
    for (size_t index = 0; index < sizeof(contents); ++index) {
        contents[index] = (unsigned char)index;
    }
    if (strcmp(argv[1], "normal") == 0) {
        assert(runtime_write_standard_output(1, NULL, 0) == 0);
        assert(acquisitions[0] == 0 && writes == 0);
        assert(runtime_write_standard_output(1, contents, sizeof(contents)) == (ssize_t)sizeof(contents));
        assert(writes == 3 && acquisitions[0] == 1);
        assert(observed_length == sizeof(contents));
        assert(memcmp(observed, contents, sizeof(contents)) == 0);
        struct iovec vectors[] = {{NULL, 0}, {"ab", 2}, {"cd", 2}};
        assert(runtime_write_standard_output_vectors(1, vectors, 3) == 4);
        assert(acquisitions[0] == 1 && writes == 5);
        assert(memcmp(observed + sizeof(contents), "abcd", 4) == 0);
        assert(runtime_write_standard_output(2, "e", 1) == 1);
        assert(acquisitions[1] == 1);
        assert(observed[observed_length - 1] == 'e');
        assert(runtime_seek_standard_output(1, 0, 0) == -1 && errno == ESPIPE);
        assert(runtime_close_standard_output(1) == 0 && drops[0] == 1);
        assert(runtime_close_standard_output(2) == 0 && drops[1] == 1);
        assert(runtime_close_standard_output(1) == -1 && errno == EBADF);
        assert(runtime_write_standard_output(1, "x", 1) == -1 && errno == EBADF);
        assert(drops[0] == 1 && error_drops == 0);
        return 0;
    }
    if (strcmp(argv[1], "validation") == 0) {
        assert(runtime_write_standard_output(0, "x", 1) == -1 && errno == EBADF);
        assert(runtime_write_standard_output(3, "x", 1) == -1 && errno == EBADF);
        assert(runtime_write_standard_output(1, contents, SIZE_MAX) == -1 && errno == EINVAL);
        assert(runtime_write_standard_output(1, NULL, 1) == -1 && errno == EFAULT);
        assert(runtime_write_standard_output_vectors(0, NULL, 0) == -1 && errno == EBADF);
        assert(runtime_write_standard_output_vectors(1, NULL, -1) == -1 && errno == EINVAL);
        assert(runtime_write_standard_output_vectors(1, NULL, 1) == -1 && errno == EFAULT);
        assert(runtime_write_standard_output_vectors(1, NULL, 0) == 0);
        struct iovec vectors[] = {{"x", 1}, {contents, SIZE_MAX >> 1}};
        assert(runtime_write_standard_output_vectors(1, vectors, 2) == -1 && errno == EINVAL);
        vectors[1] = (struct iovec){NULL, 1};
        assert(runtime_write_standard_output_vectors(1, vectors, 2) == -1 && errno == EFAULT);
        assert(writes == 0 && acquisitions[0] == 0 && acquisitions[1] == 0);
        assert(runtime_seek_standard_output(3, 0, 0) == -1 && errno == EBADF);
        assert(runtime_close_standard_output(0) == -1 && errno == EBADF);
        assert(runtime_close_standard_output(1) == 0 && drops[0] == 0);
        assert(runtime_write_standard_output_vectors(1, NULL, 0) == -1 && errno == EBADF);
        return 0;
    }
    if (strcmp(argv[1], "invalid-result") == 0 || strcmp(argv[1], "invalid-error") == 0) {
        fail_on_write = 1;
        failure_tag = strcmp(argv[1], "invalid-result") == 0 ? 2 : 1;
        failure_kind = 2;
        expect_abort = 1;
        if (setjmp(abort_target) == 0) {
            (void)runtime_write_standard_output(1, "x", 1);
            _Exit(1);
        }
        expect_abort = 0;
        assert(writes == 1 && observed_length == 0 && error_drops == 0);
        assert(runtime_close_standard_output(1) == 0 && drops[0] == 1);
        return 0;
    }

    struct iovec vectors[] = {{"prefix", 6}, {contents, sizeof(contents)}};
    ssize_t expected;
    if (strcmp(argv[1], "failed-first") == 0) {
        fail_on_write = 1;
        expected = -1;
    } else if (strcmp(argv[1], "failed-second") == 0) {
        fail_on_write = 2;
        expected = 6;
    } else if (strcmp(argv[1], "failed-partial") == 0) {
        fail_on_write = 3;
        expected = 6 + 4096;
    } else {
        assert(strcmp(argv[1], "closed") == 0);
        fail_on_write = 1;
        failure_kind = 1;
        expected = -1;
    }
    assert(runtime_write_standard_output_vectors(1, vectors, 2) == expected);
    assert(errno == (failure_kind == 1 ? EPIPE : EIO));
    assert(writes == fail_on_write);
    assert(observed_length == (expected < 0 ? 0 : (size_t)expected));
    if (expected > 0) {
        assert(memcmp(observed, "prefix", 6) == 0);
    }
    if (expected > 6) {
        assert(memcmp(observed + 6, contents, (size_t)expected - 6) == 0);
    }
    assert(error_drops == (failure_kind == 1 ? 0u : 1u));
    assert(runtime_close_standard_output(1) == 0 && drops[0] == 1);
    return 0;
}
