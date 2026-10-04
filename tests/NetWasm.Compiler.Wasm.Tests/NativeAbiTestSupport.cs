using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

internal static class NativeAbiTestSupport
{
    public static INativeAbiPlanner ScalarPlanner() =>
        new NativeAbiPlanner(new NativeDeclarationValidator(), ScalarSignaturePlanner());

    public static INativeAbiSignaturePlanner ScalarSignaturePlanner() =>
        new NativeAbiSignaturePlanner(new RejectingAggregates());

    public static INativeCallbackPlanBuilder CallbackPlanner() =>
        new NativeCallbackPlanBuilder(
            new NativeCallbackDeclarationValidator(),
            ScalarSignaturePlanner());

    public static NativeAbiPlan Plan(NativeImportDeclaration import, MethodSignatureModel logical,
        MethodSignatureModel physical) => new(import, new(logical, physical,
            [.. logical.ParameterSignatureTypes.Select((type, index) => new NativeAbiParameterPlan(index, index,
                new(NativeAbiValueKind.Scalar, type, physical.ParameterSignatureTypes[index])))],
            new(logical.ReturnType == CliValueKind.Void ? NativeAbiValueKind.Void : NativeAbiValueKind.Scalar,
                logical.ReturnSignatureType, physical.ReturnType == CliValueKind.Void ? null : physical.ReturnSignatureType), null));

    private sealed class RejectingAggregates : INativeAggregateAbiPlanner
    {
        public NativeAbiValuePlan Plan(CliTypeIdentity type, string methodName) =>
            throw new CompilerException(new(DiagnosticCode.NativeInterop, "Unqualified fixture aggregate.", methodName));
    }
}
