using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmCoreModuleValidator
{
    void Validate(string path);
}

public sealed class WasmCoreModuleValidator(
    IComponentPackageOperationRunner operations) : IWasmCoreModuleValidator
{
    private readonly IComponentPackageOperationRunner _operations = operations ??
        throw new ArgumentNullException(nameof(operations));

    public void Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _operations.Run(["validate", path, "--features", "all"],
            "validate the linked core module");
    }
}
