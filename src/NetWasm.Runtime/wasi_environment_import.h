#ifndef NETWASM_WASI_ENVIRONMENT_IMPORT_H
#define NETWASM_WASI_ENVIRONMENT_IMPORT_H

#include <stddef.h>
#include "wasi_import_attributes.h"

/* Canonical list<tuple<string, string>>; all addresses and lengths use the
 * selected core module's pointer width. The caller owns the lowered buffers. */
typedef struct {
    char *name;
    size_t name_length;
    char *value;
    size_t value_length;
} NetWasmWasiEnvironmentEntry;

typedef struct {
    NetWasmWasiEnvironmentEntry *entries;
    size_t length;
} NetWasmWasiEnvironment;

NETWASM_WASI_IMPORT("wasi:cli/environment@0.2", "get-environment")
void runtime_get_wasi_environment(NetWasmWasiEnvironment *result);

#endif
