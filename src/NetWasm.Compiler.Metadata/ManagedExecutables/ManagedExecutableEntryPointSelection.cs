using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Metadata.ManagedExecutables;

public sealed record ManagedExecutableEntryPointSelection(
    string TypeName,
    string MethodName,
    int MetadataToken,
    ManagedExecutableEntryPointAbi Abi);
