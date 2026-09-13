using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawCanonicalImportIdentityFormatter
{
    RawCanonicalImportIdentity Format(CanonicalAbiFunction abiFunction, WasmTarget target);
}

public sealed class RawCanonicalImportIdentityFormatter : IRawCanonicalImportIdentityFormatter
{
    public RawCanonicalImportIdentity Format(CanonicalAbiFunction abiFunction, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(abiFunction);
        ArgumentNullException.ThrowIfNull(abiFunction.InterfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(abiFunction.FunctionName);
        return new(CanonicalAbiNames.ImportModule(abiFunction, target), CanonicalAbiNames.ImportName(abiFunction));
    }
}
