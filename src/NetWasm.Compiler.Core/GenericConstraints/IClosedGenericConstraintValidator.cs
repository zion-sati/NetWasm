namespace NetWasm.Compiler.Core.GenericConstraints;

public interface IClosedGenericConstraintValidator
{
    void Validate(MethodInstanceModel method);
    void Validate(CliTypeIdentity type);
}
