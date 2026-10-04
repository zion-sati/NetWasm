namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorConstraintValidator
{
    bool Validate(UnsafeAccessorGenericConstraints accessor, UnsafeAccessorGenericConstraints target);
}
