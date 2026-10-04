using System;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmCoreModuleValidator
{
    void Validate(string path, ComponentTarget target);
}

public sealed class WasmCoreModuleValidator(
    IComponentPackageOperationRunner operations) : IWasmCoreModuleValidator
{
    private readonly IComponentPackageOperationRunner _operations = operations ??
        throw new ArgumentNullException(nameof(operations));

    private const string SupportedFeatures =
        "mvp,mutable-global,saturating-float-to-int,sign-extension,reference-types," +
        "multi-value,bulk-memory,exceptions,multi-memory";

    public void Validate(string path, ComponentTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Width is not ("wasm32" or "wasm64"))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        var features = target.Width == "wasm64"
            ? SupportedFeatures + ",memory64"
            : SupportedFeatures;
        _operations.Run(["validate", path, "--features", features],
            "validate the linked core module");
    }
}
