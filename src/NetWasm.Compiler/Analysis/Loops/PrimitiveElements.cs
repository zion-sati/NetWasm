using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

// The byte size of a primitive array or span element, by its name.
internal static class PrimitiveElements
{
    // Pointer-sized elements are -1.
    public static int? Size(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:bool" or "primitive:i1" or "primitive:u1" => 1,
        "primitive:char" or "primitive:i2" or "primitive:u2" => 2,
        "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
        "primitive:i8" or "primitive:u8" or "primitive:f8" => 8,
        "primitive:nativeint" or "primitive:nativeuint" => -1,
        _ => null,
    };

    public static bool IsInteger(CliTypeIdentity type) =>
        type.StackKind is CliValueKind.I4 or CliValueKind.I8 && Size(type) is > 0;
}
