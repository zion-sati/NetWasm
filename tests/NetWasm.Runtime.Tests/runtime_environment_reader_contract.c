#include <assert.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "runtime_environment_reader.h"
#include "wasi_environment_import.h"

static NetWasmWasiEnvironment input;
static unsigned reads;
static unsigned allocations;
static unsigned releases;
static uintptr_t released[9];
static int fail_allocation;
static int failure_expected;
static jmp_buf failure;
static void *storage;

void runtime_get_wasi_environment(NetWasmWasiEnvironment *result)
{
    ++reads;
    *result = input;
}

/* This test is linked with a C-allocation substitution for the reader only. */
void *runtime_test_environment_allocate(size_t size)
{
    assert(size <= 512);
    ++allocations;
    storage = fail_allocation ? NULL : malloc(size);
    assert(fail_allocation || storage != NULL);
    if (storage != NULL) {
        memset(storage, 0xa5, size);
    }
    return storage;
}

void component_free(uintptr_t address)
{
    assert(releases < sizeof(released) / sizeof(released[0]));
    released[releases++] = address;
}

void abort(void)
{
    if (!failure_expected) {
        _Exit(1);
    }
    longjmp(failure, 1);
}

static void reset(NetWasmWasiEnvironmentEntry *entries, size_t length)
{
    input = (NetWasmWasiEnvironment){entries, length};
    reads = allocations = releases = 0;
    fail_allocation = failure_expected = 0;
    free(storage);
    storage = NULL;
}

static void expect_failure(unsigned expected_allocations)
{
    failure_expected = 1;
    if (setjmp(failure) == 0) {
        (void)runtime_read_environment();
        _Exit(1);
    }
    failure_expected = 0;
    assert(reads == 1);
    assert(allocations == expected_allocations);
    assert(releases == 0);
}

int main(void)
{
    reset((NetWasmWasiEnvironmentEntry *)(uintptr_t)sizeof(void *), 0);
    char **result = runtime_read_environment();
    assert(result == storage);
    assert(result[0] == NULL);
    assert(reads == 1 && allocations == 1 && releases == 0);

    char unicode[] = "caf\xc3\xa9=\xe6\xb0\xb4";
    NetWasmWasiEnvironmentEntry entries[] = {
        {"APP_MODE", 8, "test", 4},
        {"EMPTY", 5, (char *)(uintptr_t)1, 0},
        {"UNICODE", 7, unicode, sizeof(unicode) - 1},
        {"APP_MODE", 8, "second", 6},
    };
    reset(entries, 4);
    result = runtime_read_environment();
    assert(strcmp(result[0], "APP_MODE=test") == 0);
    assert(strcmp(result[1], "EMPTY=") == 0);
    assert(strcmp(result[2], "UNICODE=caf\xc3\xa9=\xe6\xb0\xb4") == 0);
    assert(strcmp(result[3], "APP_MODE=second") == 0);
    assert(result[4] == NULL);
    assert(reads == 1 && allocations == 1 && releases == 8);
    uintptr_t expected_releases[] = {
        (uintptr_t)entries[0].name,
        (uintptr_t)entries[0].value,
        (uintptr_t)entries[1].name,
        (uintptr_t)entries[2].name,
        (uintptr_t)entries[2].value,
        (uintptr_t)entries[3].name,
        (uintptr_t)entries[3].value,
        (uintptr_t)entries,
    };
    assert(memcmp(released, expected_releases, sizeof(expected_releases)) == 0);

    reset(NULL, SIZE_MAX);
    expect_failure(0);
    NetWasmWasiEnvironmentEntry entry = {"K", SIZE_MAX, "", 0};
    reset(&entry, 1);
    expect_failure(0);
    entry = (NetWasmWasiEnvironmentEntry){"K", 1, "", SIZE_MAX - 2};
    reset(&entry, 1);
    expect_failure(0);
    entry = (NetWasmWasiEnvironmentEntry){"long-name", 9, "", 0};
    reset(&entry, SIZE_MAX / sizeof(char *) - 1);
    expect_failure(0);

    entry = (NetWasmWasiEnvironmentEntry){NULL, 0, "value", 5};
    reset(&entry, 1);
    expect_failure(0);
    entry = (NetWasmWasiEnvironmentEntry){"N\0M", 3, "value", 5};
    reset(&entry, 1);
    expect_failure(0);
    entry = (NetWasmWasiEnvironmentEntry){"N=M", 3, "value", 5};
    reset(&entry, 1);
    expect_failure(0);
    entry = (NetWasmWasiEnvironmentEntry){"K", 1, "V\0V", 3};
    reset(&entry, 1);
    expect_failure(0);

    reset(NULL, 0);
    fail_allocation = 1;
    expect_failure(1);
    return 0;
}
