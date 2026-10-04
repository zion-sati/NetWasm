namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiParameterPlan(
    int LogicalIndex,
    int? PhysicalIndex,
    NativeAbiValuePlan Value);
