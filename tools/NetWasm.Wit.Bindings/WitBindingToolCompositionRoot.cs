namespace NetWasm.Wit.Bindings;

internal static class WitBindingToolCompositionRoot
{
    public static IWitBindingCommand Create()
    {
        var documents = new WitDocumentReader(
            new WasmToolsProcess(new SystemExternalCommandRunner()));
        return new WitBindingCommand(
            new WitBindingOptionsReader(),
            documents,
            WitBindingCompositionRoot.Create(),
            new WitBindingSourceAccessibilityRewriter(),
            new SystemTextFileWriter());
    }
}
