using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodImplementationResolverFactory
{
    IMethodImplementationResolver Create(MetadataCompilationSnapshot snapshot);
}
