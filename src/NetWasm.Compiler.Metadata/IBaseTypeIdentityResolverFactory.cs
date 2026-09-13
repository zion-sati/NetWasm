using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IBaseTypeIdentityResolverFactory
{
    IBaseTypeIdentityResolver Create(MetadataCompilationSnapshot snapshot);
}
