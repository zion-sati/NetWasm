namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal enum CallEmissionKind
{
    DelegateInvoke,
    VirtualDispatch,
    SharedObjectDispatch,
    RuntimeIntrinsic,
    AsyncJSImport,
    JavaScriptImport,
    Direct,
    Native,
}
