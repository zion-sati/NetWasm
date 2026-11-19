# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

# The __c_longjmp Wasm exception tag that setjmp/longjmp throw and catch, with the payload pointer as its parameter.
# Archives from older Emscripten versions define it weakly themselves; newer ones expect it from compiler-rt, whose
# build here has no Wasm exception handling. Weak, so only one definition remains.

#ifdef __wasm64__
#define NETWASM_POINTER i64
#else
#define NETWASM_POINTER i32
#endif

.globl __c_longjmp
.weak __c_longjmp
.tagtype __c_longjmp NETWASM_POINTER
__c_longjmp:
