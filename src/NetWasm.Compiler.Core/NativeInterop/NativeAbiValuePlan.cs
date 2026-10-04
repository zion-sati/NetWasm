namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiValuePlan(
    NativeAbiValueKind Kind,
    CliTypeIdentity LogicalType,
    CliTypeIdentity? PhysicalType,
    int Size = 0,
    int Alignment = 1,
    CliTypeIdentity? ScalarStorageType = null,
    int ScalarOffset = 0);
