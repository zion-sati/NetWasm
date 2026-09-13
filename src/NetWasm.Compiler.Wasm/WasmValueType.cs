using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm;

internal enum WasmValueType : byte
{
    I32 = 0x7f,
    I64 = 0x7e,
    F32 = 0x7d,
    F64 = 0x7c,
}

internal static class WasmValueTypes
{
    public static WasmValueType FromCli(
        CliValueKind type,
        WasmTargetLayout target) => type switch
        {
            CliValueKind.I4 or CliValueKind.ValueType => WasmValueType.I32,
            CliValueKind.I8 => WasmValueType.I64,
            CliValueKind.F4 => WasmValueType.F32,
            CliValueKind.F8 => WasmValueType.F64,
            CliValueKind.NativeInt => target.UsesMemory64 ? WasmValueType.I64 : WasmValueType.I32,
            CliValueKind.ManagedReference or CliValueKind.ManagedAddress =>
                target.UsesMemory64 ? WasmValueType.I64 : WasmValueType.I32,
            CliValueKind.Void => throw new ArgumentOutOfRangeException(
                nameof(type), type, "Void is not a Wasm value type."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(type), type, "The CLI stack kind has no Wasm value type."),
        };
}
