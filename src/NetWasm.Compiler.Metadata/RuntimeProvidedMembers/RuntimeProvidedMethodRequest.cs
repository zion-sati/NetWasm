using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal sealed record RuntimeProvidedMethodRequest(
    AssemblyIdentity SourceAssembly,
    int MethodToken,
    int DeclaringTypeToken,
    CliTypeIdentity DeclaringType,
    string Name,
    bool IsInstance,
    MethodSignatureModel Signature,
    string MethodDisplayName,
    int IlOffset);
