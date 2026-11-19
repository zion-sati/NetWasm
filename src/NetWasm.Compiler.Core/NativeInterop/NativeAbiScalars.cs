// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

// The blittable primitives the C ABI passes as one Wasm value: the 8-, 16-, 32- and 64-bit
// integers, float, double and the pointer-width integers. A narrow integer travels as an i32 the
// sender has extended, as clang's Wasm C ABI and managed code both keep it. bool and char are among
// them only under disabled runtime marshalling, as C's one-byte bool and char16_t (NativeMarshallingRules).
public static class NativeAbiScalars
{
    public static bool IsBlittable(CliTypeIdentity type) =>
        type.CanonicalName is "primitive:i1" or "primitive:u1" or
            "primitive:i2" or "primitive:u2" or "primitive:i4" or "primitive:u4" or
            "primitive:i8" or "primitive:u8" or "primitive:f4" or "primitive:f8" or
            "primitive:nativeint" or "primitive:nativeuint";

    public static bool IsBlittable(CliTypeIdentity type, NativeMarshallingRules rules) =>
        IsBlittable(type) ||
        rules == NativeMarshallingRules.Disabled && type.CanonicalName is "primitive:bool" or "primitive:char";
}
