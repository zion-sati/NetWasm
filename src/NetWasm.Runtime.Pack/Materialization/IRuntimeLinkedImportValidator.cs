using System.Collections.Generic;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeLinkedImportValidator
{
    void Validate(
        RuntimeNativeValidationProfile profile,
        RuntimeLinkedModule module,
        RuntimeNativeCallbackSupport? callbackSupport = null,
        IReadOnlySet<string>? importModules = null);
}
