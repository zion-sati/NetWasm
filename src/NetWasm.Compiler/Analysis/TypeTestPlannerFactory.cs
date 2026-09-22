using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal sealed class TypeTestPlannerFactory(
    INullableTypeResolver nullableTypes) : ITypeTestPlannerFactory
{
    public ITypeTestPlanner Create(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships) =>
        new TypeTestPlanner(typeOperands, relationships, nullableTypes);
}
