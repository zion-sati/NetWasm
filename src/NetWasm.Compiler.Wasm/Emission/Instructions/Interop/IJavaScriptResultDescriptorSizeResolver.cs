using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptResultDescriptorSizeResolver
{
    int Resolve(CliTypeIdentity result);
}
