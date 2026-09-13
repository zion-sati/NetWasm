using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptImportParameterTypeResolver(
    ITargetLayout layouts) : IJavaScriptImportParameterTypeResolver
{
    public CliValueKind Resolve(CliTypeIdentity type, bool isCallback) =>
        isCallback || InteropTypeClassifier.IsHostObject(type)
            ? CliValueKind.I4
            : type.StackKind == CliValueKind.NativeInt
                ? layouts.Target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4
                : type.StackKind;
}
