#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "runtime_environment_reader.h"
#include "wasi_environment_import.h"

/* The existing runtime ABI releases buffers supplied by canonical lowering. */
void component_free(uintptr_t address);

char **runtime_read_environment(void)
{
    NetWasmWasiEnvironment environment = {0};
    runtime_get_wasi_environment(&environment);
    if (environment.length > SIZE_MAX / sizeof(char *) - 1) {
        abort();
    }
    size_t size = (environment.length + 1) * sizeof(char *);
    for (size_t index = 0; index < environment.length; ++index) {
        const NetWasmWasiEnvironmentEntry *entry = &environment.entries[index];
        if (entry->name_length > SIZE_MAX - 2 ||
            entry->value_length > SIZE_MAX - 2 - entry->name_length) {
            abort();
        }
        size_t entry_size = entry->name_length + entry->value_length + 2;
        if (entry_size > SIZE_MAX - size) {
            abort();
        }
        if (entry->name_length == 0 ||
            memchr(entry->name, '\0', entry->name_length) != NULL ||
            memchr(entry->name, '=', entry->name_length) != NULL ||
            (entry->value_length != 0 &&
             memchr(entry->value, '\0', entry->value_length) != NULL)) {
            abort();
        }
        size += entry_size;
    }
    char **result = malloc(size);
    if (result == NULL) {
        abort();
    }
    char *cursor = (char *)(result + environment.length + 1);
    for (size_t index = 0; index < environment.length; ++index) {
        const NetWasmWasiEnvironmentEntry *entry = &environment.entries[index];
        result[index] = cursor;
        memcpy(cursor, entry->name, entry->name_length);
        cursor += entry->name_length;
        *cursor++ = '=';
        if (entry->value_length != 0) {
            memcpy(cursor, entry->value, entry->value_length);
            cursor += entry->value_length;
        }
        *cursor++ = '\0';
        component_free((uintptr_t)entry->name);
        if (entry->value_length != 0) {
            component_free((uintptr_t)entry->value);
        }
    }
    result[environment.length] = NULL;
    if (environment.length != 0) {
        component_free((uintptr_t)environment.entries);
    }
    return result;
}
