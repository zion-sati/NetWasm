#include <assert.h>
#include <errno.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "runtime_standard_output.h"
#include "wasi_output_imports.h"

/* In-memory WASI 0.3 stdout and stderr: each write-via-stream stream copies at most
 * 4096 bytes a call; a scripted copy blocks and completes through the waitable set,
 * ends because the reader dropped, or reports a result the runtime does not expect. */

enum { STDOUT_READABLE = 20, STDOUT_WRITABLE = 21, STDOUT_DONE = 22,
       STDERR_READABLE = 30, STDERR_WRITABLE = 31, STDERR_DONE = 32, SET = 5 };

static unsigned streams[2];
static unsigned writable_drops[2];
static unsigned future_reads[2];
static unsigned future_drops[2];
static unsigned writes;
static unsigned waits;
static unsigned block_on_write;
static unsigned drop_on_write;
static size_t drop_after;
static unsigned bad_result_on_write;
static int block_future;
static int wrong_event;
static uint32_t pending_waitable;
static uint32_t pending_payload;
static unsigned char observed[16384];
static size_t observed_length;
static int expect_abort;
static jmp_buf abort_target;

static uint64_t stream_new(int index, uint32_t readable, uint32_t writable)
{
    streams[index]++;
    return (uint64_t)readable | ((uint64_t)writable << 32);
}

static uint32_t write_via_stream(uint32_t readable, uint32_t expected, uint32_t done)
{
    assert(readable == expected);
    return done;
}

static uint32_t stream_write(uint32_t writable, const void *contents, size_t length)
{
    assert(writable == STDOUT_WRITABLE || writable == STDERR_WRITABLE);
    assert(length > 0);
    writes++;
    size_t count = length > 4096 ? 4096 : length;
    if (writes == bad_result_on_write) {
        return 2u;
    }
    if (writes == drop_on_write) {
        memcpy(observed + observed_length, contents, drop_after);
        observed_length += drop_after;
        return (uint32_t)(drop_after << 4) | NETWASM_WASI_COPY_DROPPED;
    }
    memcpy(observed + observed_length, contents, count);
    observed_length += count;
    if (writes == block_on_write) {
        pending_waitable = writable;
        pending_payload = (uint32_t)(count << 4) | NETWASM_WASI_COPY_COMPLETED;
        return NETWASM_WASI_OUTPUT_BLOCKED;
    }
    return (uint32_t)(count << 4) | NETWASM_WASI_COPY_COMPLETED;
}

static uint32_t future_read(int index, uint32_t readable, uint32_t expected, void *result)
{
    assert(readable == expected && result != NULL);
    future_reads[index]++;
    if (block_future) {
        pending_waitable = readable;
        pending_payload = 1u << 4;
        return NETWASM_WASI_OUTPUT_BLOCKED;
    }
    return 1u << 4;
}

uint64_t runtime_wasi_stdout_stream_new(void) { return stream_new(0, STDOUT_READABLE, STDOUT_WRITABLE); }
uint32_t runtime_wasi_stdout_write_via_stream(uint32_t readable) { return write_via_stream(readable, STDOUT_READABLE, STDOUT_DONE); }
uint32_t runtime_wasi_stdout_stream_write(uint32_t writable, const void *contents, size_t length) { return stream_write(writable, contents, length); }
void runtime_wasi_stdout_stream_drop_writable(uint32_t writable) { assert(writable == STDOUT_WRITABLE); writable_drops[0]++; }
uint32_t runtime_wasi_stdout_future_read(uint32_t readable, void *result) { return future_read(0, readable, STDOUT_DONE, result); }
void runtime_wasi_stdout_future_drop_readable(uint32_t readable) { assert(readable == STDOUT_DONE); future_drops[0]++; }
uint64_t runtime_wasi_stderr_stream_new(void) { return stream_new(1, STDERR_READABLE, STDERR_WRITABLE); }
uint32_t runtime_wasi_stderr_write_via_stream(uint32_t readable) { return write_via_stream(readable, STDERR_READABLE, STDERR_DONE); }
uint32_t runtime_wasi_stderr_stream_write(uint32_t writable, const void *contents, size_t length) { return stream_write(writable, contents, length); }
void runtime_wasi_stderr_stream_drop_writable(uint32_t writable) { assert(writable == STDERR_WRITABLE); writable_drops[1]++; }
uint32_t runtime_wasi_stderr_future_read(uint32_t readable, void *result) { return future_read(1, readable, STDERR_DONE, result); }
void runtime_wasi_stderr_future_drop_readable(uint32_t readable) { assert(readable == STDERR_DONE); future_drops[1]++; }

uint32_t runtime_wasi_waitable_set_new(void) { return SET; }

void runtime_wasi_waitable_join(uint32_t waitable, uint32_t set)
{
    assert(waitable == pending_waitable && (set == SET || set == 0));
}

uint32_t runtime_wasi_waitable_set_wait(uint32_t set, uint32_t *event)
{
    assert(set == SET);
    waits++;
    event[0] = wrong_event ? pending_waitable + 100 : pending_waitable;
    event[1] = pending_payload;
    return 0;
}

void abort(void)
{
    if (!expect_abort) { _Exit(1); }
    longjmp(abort_target, 1);
}

static void expect_abort_from(void (*operation)(void))
{
    expect_abort = 1;
    if (setjmp(abort_target) == 0) {
        operation();
        _Exit(1);
    }
    expect_abort = 0;
}

static void write_one(void) { (void)runtime_write_standard_output(1, "x", 1); }

int main(int argc, char **argv)
{
    assert(argc == 2);
    unsigned char contents[8193];
    for (size_t index = 0; index < sizeof(contents); ++index) {
        contents[index] = (unsigned char)index;
    }
    if (strcmp(argv[1], "normal") == 0) {
        /* A write opens the stream once, copies in pieces (the second blocks and
         * completes through the set), and closing ends the stream and reads its future. */
        block_on_write = 2;
        assert(runtime_write_standard_output(1, NULL, 0) == 0);
        assert(streams[0] == 0 && writes == 0);
        assert(runtime_write_standard_output(1, contents, sizeof(contents)) == (ssize_t)sizeof(contents));
        assert(writes == 3 && streams[0] == 1 && waits == 1);
        assert(observed_length == sizeof(contents));
        assert(memcmp(observed, contents, sizeof(contents)) == 0);
        struct iovec vectors[] = {{NULL, 0}, {"ab", 2}, {"cd", 2}};
        assert(runtime_write_standard_output_vectors(1, vectors, 3) == 4);
        assert(streams[0] == 1 && writes == 5);
        assert(memcmp(observed + sizeof(contents), "abcd", 4) == 0);
        assert(runtime_write_standard_output(2, "e", 1) == 1);
        assert(streams[1] == 1);
        assert(observed[observed_length - 1] == 'e');
        assert(runtime_seek_standard_output(1, 0, 0) == -1 && errno == ESPIPE);
        assert(runtime_close_standard_output(1) == 0);
        assert(writable_drops[0] == 1 && future_reads[0] == 1 && future_drops[0] == 1);
        block_future = 1;
        pending_waitable = STDERR_DONE;
        assert(runtime_close_standard_output(2) == 0);
        assert(writable_drops[1] == 1 && future_reads[1] == 1 && future_drops[1] == 1 && waits == 2);
        assert(runtime_close_standard_output(1) == -1 && errno == EBADF);
        assert(runtime_write_standard_output(1, "x", 1) == -1 && errno == EBADF);
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
        assert(writes == 0 && streams[0] == 0 && streams[1] == 0);
        assert(runtime_seek_standard_output(3, 0, 0) == -1 && errno == EBADF);
        assert(runtime_close_standard_output(0) == -1 && errno == EBADF);
        /* Closing an output that was never written opens nothing. */
        assert(runtime_close_standard_output(1) == 0 && writable_drops[0] == 0 && future_reads[0] == 0);
        assert(runtime_write_standard_output_vectors(1, NULL, 0) == -1 && errno == EBADF);
        return 0;
    }
    if (strcmp(argv[1], "invalid-result") == 0) {
        /* A copy that reports neither completion nor a dropped reader is a broken host. */
        bad_result_on_write = 1;
        expect_abort_from(write_one);
        assert(writes == 1 && observed_length == 0);
        return 0;
    }
    if (strcmp(argv[1], "wrong-event") == 0) {
        /* A wait that wakes for another waitable is a broken host. */
        block_on_write = 1;
        wrong_event = 1;
        expect_abort_from(write_one);
        assert(writes == 1 && waits == 1);
        return 0;
    }

    /* The reader dropping ends the output: what was copied counts, then EPIPE. */
    struct iovec vectors[] = {{"prefix", 6}, {contents, sizeof(contents)}};
    ssize_t expected;
    if (strcmp(argv[1], "dropped-first") == 0) {
        drop_on_write = 1;
        drop_after = 0;
        expected = -1;
    } else if (strcmp(argv[1], "dropped-second") == 0) {
        drop_on_write = 2;
        drop_after = 0;
        expected = 6;
    } else {
        assert(strcmp(argv[1], "dropped-partial") == 0);
        drop_on_write = 3;
        drop_after = 100;
        expected = 6 + 4096 + 100;
    }
    assert(runtime_write_standard_output_vectors(1, vectors, 2) == expected);
    assert(writes == drop_on_write);
    assert(observed_length == (expected < 0 ? 0 : (size_t)expected));
    if (expected > 0) {
        assert(memcmp(observed, "prefix", 6) == 0);
    }
    if (expected > 6) {
        assert(memcmp(observed + 6, contents, (size_t)expected - 6) == 0);
    }
    if (expected < 0) {
        assert(errno == EPIPE);
    }
    assert(runtime_write_standard_output(1, "x", 1) == -1 && errno == EPIPE);
    assert(writes == drop_on_write);
    assert(runtime_close_standard_output(1) == 0 && writable_drops[0] == 1 && future_drops[0] == 1);
    return 0;
}
