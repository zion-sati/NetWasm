using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Validation;

internal interface IMetadataInvariantValidator
{
    void Validate(
        MetadataCompilationSnapshot metadata,
        ITypeRepository types,
        ISymbolFormatter symbols);
}
