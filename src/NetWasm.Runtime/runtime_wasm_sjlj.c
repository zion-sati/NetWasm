// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Runtime support for native code built with Wasm setjmp/longjmp (Emscripten's -sSUPPORT_LONGJMP=wasm, which its
// archives use). longjmp throws the __c_longjmp tag (runtime_wasm_longjmp_tag.S) with a pointer to the jmp_buf and the
// value; the setjmp-calling function catches it and asks whether that jmp_buf is one of its own.
//
// Two ABIs reach this: archives built by Emscripten 3.1.57 and later call __wasm_setjmp and __wasm_setjmp_test, which
// Emscripten's compiler-rt provides; older ones call saveSetjmp and testSetjmp, which keep a per-call table of
// (id, label) pairs that the function allocates and frees itself. Both store an identity in the jmp_buf's first word,
// so the payload this longjmp throws, kept in the jmp_buf as Emscripten's own does, serves both. Every definition is
// weak, so an archive that brings its own wins, and a program whose native code never calls them links none of them.

#include <stdint.h>
#include <stdlib.h>

struct netwasm_wasm_longjmp_arguments
{
    void *env;
    int value;
};

// The fields of jmp_buf that setjmp/longjmp use; Emscripten's jmp_buf is six words and larger.
struct netwasm_wasm_jmp_buf
{
    void *function_invocation;
    uint32_t label;
    struct netwasm_wasm_longjmp_arguments arguments;
};

struct netwasm_setjmp_table_entry
{
    uintptr_t id;
    uint32_t label;
};

enum { NETWASM_C_LONGJMP_TAG = 1 };

// Emscripten's compiler-rt (emscripten_tempret.s): the table's new size goes back through it.
extern void setTempRet0(uint32_t value);

static uintptr_t netwasm_setjmp_id;

__attribute__((weak)) void __wasm_longjmp(void *env, int value)
{
    struct netwasm_wasm_jmp_buf *buffer = env;
    buffer->arguments.env = env;
    // setjmp never returns 0 for a longjmp.
    buffer->arguments.value = value == 0 ? 1 : value;
    __builtin_wasm_throw(NETWASM_C_LONGJMP_TAG, &buffer->arguments);
}

// Records a setjmp: a fresh id in the jmp_buf and (id, label) in the calling function's table, which ends at the first
// zero id. A full table is doubled (its size excludes the terminator). Returns the table and its size.
__attribute__((weak)) struct netwasm_setjmp_table_entry *saveSetjmp(
    uintptr_t *env, uint32_t label, struct netwasm_setjmp_table_entry *table, uint32_t size)
{
    uintptr_t id = ++netwasm_setjmp_id;
    *env = id;
    uint32_t index = 0;
    while (index < size && table[index].id != 0)
    {
        index++;
    }
    if (index == size)
    {
        size *= 2;
        table = realloc(table, sizeof(*table) * (size + 1));
        if (table == NULL)
        {
            abort();
        }
    }
    table[index].id = id;
    table[index].label = label;
    table[index + 1].id = 0;
    setTempRet0(size);
    return table;
}

// The label of the setjmp with this id in the calling function's table, or 0 if the longjmp is not for this function.
__attribute__((weak)) uint32_t testSetjmp(uintptr_t id, struct netwasm_setjmp_table_entry *table, uint32_t size)
{
    for (uint32_t index = 0; index < size && table[index].id != 0; index++)
    {
        if (table[index].id == id)
        {
            return table[index].label;
        }
    }
    return 0;
}
