namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeLinkedImportValidator
{
    void Validate(
        RuntimeNativeValidationProfile profile,
        RuntimeLinkedModule module,
        RuntimeNativeCallbackSupport? callbackSupport = null);
}
