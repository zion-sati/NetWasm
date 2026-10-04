namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeAbiPlanner
{
    NativeAbiPlan Plan(MethodInstanceModel method);
}
