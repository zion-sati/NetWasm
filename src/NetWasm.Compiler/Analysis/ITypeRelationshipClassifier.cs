using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ITypeRelationshipClassifier
{
    TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target);
}
