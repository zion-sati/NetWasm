using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed record OriginalContractType(
    MetadataAssemblySnapshot Source,
    TypeDefinitionModel Definition);
