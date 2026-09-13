using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation.Delegates;

internal interface IDelegateBindingInvariantValidator
{
    void Validate(ITypeClassifier types, ReachableProgram program);
}
