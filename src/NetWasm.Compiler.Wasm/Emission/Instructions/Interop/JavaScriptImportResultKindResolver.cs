using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptImportResultKindResolver
{
    JavaScriptImportResultKind Resolve(CliTypeIdentity type);
}

internal sealed class JavaScriptImportResultKindResolver :
    IJavaScriptImportResultKindResolver
{
    public JavaScriptImportResultKind Resolve(CliTypeIdentity type) => type switch
    {
        _ when InteropTypeClassifier.IsString(type) =>
            JavaScriptImportResultKind.String,
        _ when InteropTypeClassifier.IsByteArray(type) =>
            JavaScriptImportResultKind.ByteArray,
        _ when InteropTypeClassifier.IsHostObject(type) =>
            JavaScriptImportResultKind.HostObject,
        _ => JavaScriptImportResultKind.Scalar,
    };
}
