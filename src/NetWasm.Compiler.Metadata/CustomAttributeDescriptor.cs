using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record CustomAttributeDescriptor(
    AssemblyIdentity Source,
    int AttributeToken,
    int ConstructorToken,
    CliTypeIdentity AttributeType,
    CliGenericContext GenericContext);
