namespace NetWasm.Compiler.Core.Tests.NativeInterop;

using NetWasm.Compiler.Core.NativeInterop;

internal static class NativeAbiTestSupport
{
    public static INativeAbiSignaturePlanner ScalarSignatures() =>
        new NativeAbiSignaturePlanner(new RejectingAggregates());

    private sealed class RejectingAggregates : INativeAggregateAbiPlanner
    {
        public NativeAbiValuePlan Plan(CliTypeIdentity type, string methodName) =>
            throw new CompilerException(new(DiagnosticCode.NativeInterop, "Unqualified fixture aggregate.", methodName));
    }
}
