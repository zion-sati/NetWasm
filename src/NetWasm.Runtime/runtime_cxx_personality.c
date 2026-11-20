// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// C++ landing pads compiled by older Emscripten versions (3.1.56, which SkiaSharp's and HarfBuzzSharp's WebAssembly
// archives use, among them) call _Unwind_CallPersonality with the caught exception; newer ones call libc++abi's
// __gxx_wasm_personality_v0, which does the same: it resets the landing pad's selector and runs the personality's
// search phase. Weak, so an archive's own definition wins; the support archive is lazy, so only a program whose native
// code calls it links it.

extern int __gxx_wasm_personality_v0(void *exception);

__attribute__((weak)) int _Unwind_CallPersonality(void *exception)
{
    return __gxx_wasm_personality_v0(exception);
}
