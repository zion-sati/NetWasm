using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal interface IRootMapInvariantValidator
{
    void Validate(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        ReachableProgram program);
}
