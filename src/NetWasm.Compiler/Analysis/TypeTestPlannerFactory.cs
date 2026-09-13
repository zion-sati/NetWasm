namespace NetWasm.Compiler.Analysis;

internal sealed class TypeTestPlannerFactory : ITypeTestPlannerFactory
{
    public ITypeTestPlanner Create(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships) =>
        new TypeTestPlanner(typeOperands, relationships);
}
