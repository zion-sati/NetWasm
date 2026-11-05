using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ITypeRelationshipKeyResolver
{
    TypeRelationshipKeys Resolve(CliTypeIdentity type);
}
