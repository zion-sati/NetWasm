using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public interface ITypedStackValidatorFactory
{
    ITypedStackValidator Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods);
}
