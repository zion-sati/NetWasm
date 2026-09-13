using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class CalledMethodResolver(
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : ICalledMethodResolver
{
    public MethodInstanceModel? Resolve(CilInstruction instruction) =>
        instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or
            CilOperation.NewObject or CilOperation.LoadFunction or
            CilOperation.LoadVirtualFunction
            ? instruction.Operand switch
            {
                CilOperand.MethodInstance method => method.Value,
                CilOperand.Entity method => Resolve(methods.GetMethod(method.Key)),
                _ => null,
            }
            : null;

    private MethodInstanceModel Resolve(MethodDefinitionModel method) =>
        methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format(method),
            0);
}
