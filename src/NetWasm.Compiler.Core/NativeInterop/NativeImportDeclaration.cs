using System.Reflection;

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeImportDeclaration(
    string LibraryName,
    string EntryPoint,
    MethodImportAttributes Attributes,
    bool IsVarArg,
    bool HasMarshalling,
    bool SuppressesGcTransition,
    bool HasCustomCallingConvention);
