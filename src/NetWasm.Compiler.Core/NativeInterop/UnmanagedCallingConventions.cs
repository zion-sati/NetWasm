using System.Collections.Frozen;
using System.Collections.Generic;

namespace NetWasm.Compiler.Core.NativeInterop;

// The CallConv types a function pointer, [UnmanagedCallersOnly] or [UnmanagedCallConv] names, read
// as CoreCLR's CallConvBuilder reads them: Cdecl, Stdcall, Thiscall, Fastcall and Swift are base
// conventions, of which one may be named; SuppressGCTransition and MemberFunction modify the base;
// any other type is ignored, so a convention a later .NET adds does not fail. Desktop .NET's 64-bit
// platforms give Cdecl, Stdcall and Thiscall their one C convention, as clang does for Wasm, so each
// is the C ABI here. SuppressGCTransition is a promise a call may ignore, and MemberFunction changes
// only how Windows returns a struct from a C++ member function. Desktop .NET fails Fastcall on every
// platform and Swift on all but Apple's, so both fail here.
public static class UnmanagedCallingConventions
{
    private const string Namespace = "System.Runtime.CompilerServices.";

    // Each base convention, and whether the C ABI honours it.
    private static readonly FrozenDictionary<string, bool> Bases = new Dictionary<string, bool>
    {
        [Namespace + "CallConvCdecl"] = true,
        [Namespace + "CallConvStdcall"] = true,
        [Namespace + "CallConvThiscall"] = true,
        [Namespace + "CallConvFastcall"] = false,
        [Namespace + "CallConvSwift"] = false,
    }.ToFrozenDictionary();

    // Why the C ABI cannot honour these CallConv types (full names, null for a type without one), or
    // null when it can.
    public static string? Unsupported(IEnumerable<string?> conventions)
    {
        string? chosen = null;
        foreach (var convention in conventions)
        {
            if (convention is null || !Bases.ContainsKey(convention))
            {
                continue;
            }
            if (chosen is not null)
            {
                return $"calling conventions '{chosen}' and '{convention}' are both named; only one is supported";
            }
            chosen = convention;
        }
        return chosen is null || Bases[chosen] ? null : $"calling convention '{chosen}' is not supported";
    }
}
