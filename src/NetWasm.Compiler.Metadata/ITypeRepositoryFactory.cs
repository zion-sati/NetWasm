using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeRepositoryFactory
{
    ITypeRepository Create(MetadataCompilationSnapshot snapshot);
}
