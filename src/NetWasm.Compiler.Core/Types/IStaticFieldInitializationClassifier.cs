using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Types;

public interface IStaticFieldInitializationClassifier
{
    // Whether the accessed field's type is already initialized wherever this
    // access in the body runs, so the access needs no initialization check.
    bool IsInitializedFor(
        MethodDefinitionModel accessor,
        CliTypeIdentity? accessorDeclaringType,
        ImmutableArray<CilInstruction> body,
        CilInstruction access);
}
