using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record GenericArgumentSpecialConstraintRequest(
    CliTypeIdentity Argument, GenericParameterAttributes Attributes, string Caller, int IlOffset);

public interface IGenericArgumentSpecialConstraintValidator
{
    void Validate(GenericArgumentSpecialConstraintRequest request);
}
