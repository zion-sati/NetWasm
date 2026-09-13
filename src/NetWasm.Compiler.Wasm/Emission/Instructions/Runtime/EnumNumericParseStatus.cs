namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

/// <summary>
/// The result status written by the decimal enum numeric parser.
/// </summary>
internal enum EnumNumericParseStatus
{
    Success = 0,
    NonNumeric = 1,
    Invalid = 2,
    Overflow = 3,
}
