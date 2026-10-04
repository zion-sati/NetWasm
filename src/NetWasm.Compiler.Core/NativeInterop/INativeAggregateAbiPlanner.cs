namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeAggregateAbiPlanner
{
    NativeAbiValuePlan Plan(CliTypeIdentity type, string methodName);
}
