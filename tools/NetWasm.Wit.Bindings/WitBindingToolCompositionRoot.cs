namespace NetWasm.Wit.Bindings;

internal static class WitBindingToolCompositionRoot
{
    public static IWitBindingCommand Create()
        => Create(WitBindingToolchain.ResolveWasmToolsCommand(AppContext.BaseDirectory));

    internal static IWitBindingCommand Create(ExternalToolCommand wasmToolsCommand)
    {
        ArgumentNullException.ThrowIfNull(wasmToolsCommand);
        var wasmTools = new ProcessWasmTools(
            new SystemExternalCommandRunner(),
            wasmToolsCommand);
        var documents = new WitDocumentReader(
            wasmTools);
        return new WitBindingCommand(
            new WitBindingOptionsReader(),
            documents,
            WitBindingCompositionRoot.Create(),
            new WitBindingSourceAccessibilityRewriter(),
            new SystemTextFileWriter());
    }
}
