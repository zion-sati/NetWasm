namespace NetWasm.Compiler.Analysis.Delegates;

internal interface IDelegateBindingPlannerFactory
{
    IDelegateBindingPlanner Create(ITypeRelationshipClassifier relationships);
}
