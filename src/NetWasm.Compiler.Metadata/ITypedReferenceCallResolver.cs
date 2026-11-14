using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ITypedReferenceCallResolver
{
    // The TypedReference helper an instruction calls, over the instruction's type operand when it has one.
    MethodInstanceModel Resolve(TypedReferenceInstruction instruction, ImmutableArray<CliTypeIdentity> methodArguments);
}
