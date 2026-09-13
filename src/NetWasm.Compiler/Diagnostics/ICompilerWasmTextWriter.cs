namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerWasmTextWriter
{
    void WriteText(string wasmPath, string watPath);
}
