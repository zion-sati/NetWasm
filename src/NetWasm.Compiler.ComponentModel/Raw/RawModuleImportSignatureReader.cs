using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleImportSignatureReader
{
    ImmutableArray<RawCoreFunctionImportSignature> Read(
        RawModuleInspectionRequest request);
}

public sealed class RawModuleImportSignatureReader(
    IRawModuleInspectionProcess process,
    IRawModuleInspectionProtocolReader protocol) : IRawModuleImportSignatureReader
{
    private readonly IRawModuleInspectionProcess _process = process ??
        throw new ArgumentNullException(nameof(process));
    private readonly IRawModuleInspectionProtocolReader _protocol = protocol ??
        throw new ArgumentNullException(nameof(protocol));

    public ImmutableArray<RawCoreFunctionImportSignature> Read(
        RawModuleInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = _process.Run(request);
        ArgumentNullException.ThrowIfNull(result);
        return _protocol.Read(result);
    }
}
