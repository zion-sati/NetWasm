using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IEntryPointValidationStrategy
{
    CompilerEntryPointKind Kind { get; }

    void Validate(
        MethodDefinitionModel entryPoint,
        ISymbolFormatter symbols);
}
