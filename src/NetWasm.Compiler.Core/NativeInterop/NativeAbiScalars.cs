namespace NetWasm.Compiler.Core.NativeInterop;

// The blittable primitives the C ABI passes as one Wasm value: the 8-, 16-, 32- and 64-bit
// integers, float, double and the pointer-width integers. A narrow integer travels as an i32 the
// sender has extended, as clang's Wasm C ABI and managed code both keep it. bool and char are not
// among them: .NET marshals them.
public static class NativeAbiScalars
{
    public static bool IsBlittable(CliTypeIdentity type) =>
        type.CanonicalName is "primitive:i1" or "primitive:u1" or
            "primitive:i2" or "primitive:u2" or "primitive:i4" or "primitive:u4" or
            "primitive:i8" or "primitive:u8" or "primitive:f4" or "primitive:f8" or
            "primitive:nativeint" or "primitive:nativeuint";
}
