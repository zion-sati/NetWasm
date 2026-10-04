using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorTypeAssignability
{
    bool IsAssignable(CliTypeIdentity source, CliTypeIdentity target, UnsafeAccessorGenericConstraints constraints);
}
