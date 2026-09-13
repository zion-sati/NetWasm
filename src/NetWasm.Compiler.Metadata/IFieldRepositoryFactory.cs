using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IFieldRepositoryFactory
{
    IFieldRepository Create(MetadataCompilationSnapshot snapshot);
}
