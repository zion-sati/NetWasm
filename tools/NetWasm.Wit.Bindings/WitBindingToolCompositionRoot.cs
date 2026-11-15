// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
            new WitInputStager(Console.In),
            documents,
            WitBindingCompositionRoot.Create(),
            new WitBindingSourceAccessibilityRewriter(),
            new SystemTextFileWriter(),
            Console.Out);
    }
}
