using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodRepositoryFactory
{
    IMethodRepository Create(MetadataCompilationSnapshot snapshot);
}
