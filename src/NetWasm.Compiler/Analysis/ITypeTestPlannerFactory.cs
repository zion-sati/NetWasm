namespace NetWasm.Compiler.Analysis;

internal interface ITypeTestPlannerFactory
{
    ITypeTestPlanner Create(
        ITypeOperandResolver typeOperands,
        ITypeRelationshipClassifier relationships);
}
