namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

/// <summary>
/// Locals receiving the parsed enum value and its explicit parse status.
/// </summary>
internal readonly record struct EnumNumericParseOutput(int ValueLocal, int StatusLocal);
