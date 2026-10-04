using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record CustomAttributeMemberBinding(FieldInstanceModel? Field, MethodInstanceModel? Setter);

public interface ICustomAttributeMemberResolver
{
    CustomAttributeMemberBinding? Resolve(CliTypeIdentity declaringType, string name,
        CustomAttributeNamedArgumentKind kind);
}
