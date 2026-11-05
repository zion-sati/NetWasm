namespace NetWasm.Compiler.Wasm.Units;

/// <summary>The module index space an instruction operand names.</summary>
internal enum WasmIndexSpace
{
    Function,
    Type,
    Table,
    Memory,
    Global,
    Tag,
    Data,
    Element,
}
