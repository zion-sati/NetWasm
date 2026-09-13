using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed record RawBindingBuildResult(
    byte[] Adapter,
    ImmutableArray<WitInterfaceFunction> RequiredImports);

internal interface IRawBindingSession : IDisposable
{
    RawBindingBuildResult Build(RawBuildImportSourceValidationRequest request);
}
