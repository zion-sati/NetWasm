using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptResultDescriptorSizeResolver(
    ITargetLayout layouts) : IJavaScriptResultDescriptorSizeResolver
{
    public int Resolve(CliTypeIdentity result) => result.StackKind switch
    {
        CliValueKind.I8 or CliValueKind.F8 => sizeof(long),
        CliValueKind.NativeInt when layouts.Target.UsesMemory64 => sizeof(long),
        _ => sizeof(int),
    };
}
