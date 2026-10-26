using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed record RawBindingBuildResult(
    byte[] Adapter,
    ImmutableArray<WitInterfaceFunction> RequiredImports)
{
    // The declared import modules the final module imports from: those a
    // [WasmImportLinkage] import or a linked native archive actually uses.
    public ImmutableArray<string> ImportModules { get; init; } = [];
}

internal interface IRawBindingSession : IDisposable
{
    RawBindingBuildResult Build(RawBuildImportSourceValidationRequest request);
}
