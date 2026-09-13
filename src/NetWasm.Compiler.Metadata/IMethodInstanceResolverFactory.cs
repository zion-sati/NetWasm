using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodInstanceResolverFactory
{
    IMethodInstanceResolver Create(MetadataCompilationSnapshot snapshot);
}
