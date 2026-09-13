using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed record ConstrainedCallTargetRequest(
    CliTypeIdentity ConstrainedType,
    TypeDefinitionModel ConcreteType,
    MethodDefinitionModel Declaration,
    MethodSignatureModel CallSignature,
    ImmutableArray<CliTypeIdentity> TypeArguments,
    ImmutableArray<CliTypeIdentity> MethodArguments);
