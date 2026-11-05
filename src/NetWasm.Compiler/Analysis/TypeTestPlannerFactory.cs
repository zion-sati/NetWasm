using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeTestPlannerFactory(
    INullableTypeResolver nullableTypes) : ITypeTestPlannerFactory
{
    public ITypeTestPlanner Create(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships,
        ITypeRelationshipKeyResolver relationshipKeys) =>
        new TypeTestPlanner(typeOperands, relationships, relationshipKeys, nullableTypes);
}
