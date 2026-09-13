using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed record ManagedEntryPoint(
    string TypeName,
    string MethodName,
    int MetadataToken,
    ManagedExecutableEntryPointAbi Abi);
