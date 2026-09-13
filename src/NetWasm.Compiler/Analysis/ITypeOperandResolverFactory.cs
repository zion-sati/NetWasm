using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface ITypeOperandResolverFactory
{
    ITypeOperandResolver Create(ITypeIdentityResolver types);
}
