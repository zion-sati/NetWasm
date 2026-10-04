using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorConstraintReader
{
    UnsafeAccessorGenericConstraints Read(MethodDefinitionModel method);
}
