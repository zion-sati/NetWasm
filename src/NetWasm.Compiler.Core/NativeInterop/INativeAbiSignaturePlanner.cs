namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeAbiSignaturePlanner
{
    NativeAbiSignaturePlan Plan(
        MethodSignatureModel signature,
        NativeAbiSignatureKind kind,
        string methodName);
}
