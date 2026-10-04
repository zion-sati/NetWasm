namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiPlan(
    NativeImportDeclaration Import,
    NativeAbiSignaturePlan Lowering)
{
    public MethodSignatureModel Signature => Lowering.PhysicalSignature;
}
