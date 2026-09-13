using System;

namespace NetWasm.Hosting.Capabilities;

internal sealed class InternalImportPolicy : IInternalImportPolicy
{
    private const string ReactorHostModule = "netwasm:runtime/reactor-host";
    private const string VersionedReactorHostModule = $"{ReactorHostModule}@1.0.0";

    public bool IsInternal(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return string.Equals(module, ReactorHostModule, StringComparison.Ordinal)
            || string.Equals(module, VersionedReactorHostModule, StringComparison.Ordinal);
    }
}
