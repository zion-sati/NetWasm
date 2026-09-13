namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal enum CallEmissionKind
{
    DelegateInvoke,
    VirtualDispatch,
    RuntimeIntrinsic,
    AsyncJSImport,
    JavaScriptImport,
    Direct,
}
