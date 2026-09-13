namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal readonly record struct CapturedSlot(bool IsArgument, int Index);
