using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeOperandResolverFactory : ITypeOperandResolverFactory
{
    public ITypeOperandResolver Create(ITypeIdentityResolver types) =>
        new TypeOperandResolver(types);
}
