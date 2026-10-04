namespace NetWasm.Compiler.Core.NativeInterop;

public enum NativeAbiValueKind
{
    Void,
    Scalar,
    IgnoredAggregate,
    ScalarizedAggregate,
    IndirectAggregate,
}
