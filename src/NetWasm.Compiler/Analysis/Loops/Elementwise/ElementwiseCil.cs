using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

// Lane kinds and types, vector operators and instruction copies.
internal static class ElementwiseCil
{
    internal static string Operator(CilOperation operation) => operation switch
    {
        CilOperation.Add => "op_Addition",
        CilOperation.Subtract => "op_Subtraction",
        CilOperation.Multiply => "op_Multiply",
        CilOperation.Divide => "op_Division",
        CilOperation.BitwiseAnd => "op_BitwiseAnd",
        CilOperation.BitwiseOr => "op_BitwiseOr",
        _ => "op_ExclusiveOr",
    };

    // The lane kind of an array element type: 32-bit and 64-bit integers and
    // floating values; others are Unknown.
    internal static CliValueKind Lane(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i4" or "primitive:u4" => CliValueKind.I4,
        "primitive:i8" or "primitive:u8" => CliValueKind.I8,
        "primitive:f4" => CliValueKind.F4,
        "primitive:f8" => CliValueKind.F8,
        _ => CliValueKind.Unknown,
    };

    // The vectors' lane types are signed, as IL's integer operations are.
    internal static CliTypeIdentity LaneType(CliValueKind kind) => kind switch
    {
        CliValueKind.I4 => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        CliValueKind.I8 => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        CliValueKind.F4 => CliTypeIdentity.Primitive("f4", CliValueKind.F4),
        _ => CliTypeIdentity.Primitive("f8", CliValueKind.F8),
    };

    internal static int Size(CliValueKind kind) => kind is CliValueKind.I8 or CliValueKind.F8 ? 8 : 4;

    internal static bool IsArray(CilInstruction instruction) =>
        instruction.Operation is CilOperation.LoadLocal or CilOperation.LoadArgument;

    internal static bool SameArray(CilInstruction left, CilInstruction right) =>
        left.Operation == right.Operation && Equals(left.Operand, right.Operand);

    internal static CilInstruction Copy(CilInstruction instruction) => Instruction(instruction.Operation, instruction.Operand);

    internal static CilInstruction Instruction(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);
}
