using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class NumericComparisonEmitter
{
    public static void Emit(
        WasmTargetLayout target,
        CliValueKind type,
        Action i32Operation,
        Action i64Operation,
        Action f32Operation,
        Action f64Operation)
    {
        switch (type)
        {
            case CliValueKind.I4:
                i32Operation();
                break;
            case CliValueKind.I8:
                i64Operation();
                break;
            case CliValueKind.NativeInt:
                if (target.UsesMemory64) i64Operation();
                else i32Operation();
                break;
            case CliValueKind.F4:
                f32Operation();
                break;
            case CliValueKind.F8:
                f64Operation();
                break;
            default:
                throw new InvalidOperationException(
                    $"unsupported numeric comparison type '{type}'");
        }
    }
}
