using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeIdentityResolverFactory
{
    ITypeIdentityResolver Create(MetadataCompilationSnapshot snapshot);
}
