using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IImplementedInterfaceResolverFactory
{
    IImplementedInterfaceResolver Create(MetadataCompilationSnapshot snapshot);
}
