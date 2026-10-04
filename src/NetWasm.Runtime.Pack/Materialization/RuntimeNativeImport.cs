using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeNativeImport(
    string LibraryName,
    string EntryPoint,
    ImmutableArray<RuntimeNativeValueType> Parameters,
    RuntimeNativeValueType? ReturnType);

internal enum RuntimeNativeValueType
{
    I32,
    I64,
    F32,
    F64,
}
