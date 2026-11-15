// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Hosting.Generator;

internal static class Program
{
    internal static int Main(string[] arguments) =>
        Create().Run(arguments, Console.Out, Console.Error);

    internal static IHostingPlatformCatalogApplication Create()
    {
        var interfaceSpecifiers = new WitInterfaceSpecifierFormatter();
        return new HostingPlatformCatalogApplication(
            new TextFileStore(),
            new WitDocumentJsonReader(),
            new WitInterfaceCatalogBuilder(
                new WitWorldValidator(),
                new WitWorldSpecifierFormatter(),
                interfaceSpecifiers,
                new WitTypeIdentityFormatter(interfaceSpecifiers)),
            new HostingPlatformCatalogProjector(
                new WasiCapabilityClassifier(),
                new Sha256TextHasher()),
            new HostingPlatformCatalogJavaScriptWriter());
    }
}
