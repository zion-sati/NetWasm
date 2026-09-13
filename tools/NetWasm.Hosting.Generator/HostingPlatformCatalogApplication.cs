using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;

namespace NetWasm.Hosting.Generator;

internal interface IHostingPlatformCatalogApplication
{
    int Run(string[] arguments, TextWriter standardOutput, TextWriter standardError);
}

internal sealed class HostingPlatformCatalogApplication(
    ITextFileStore files,
    IWitDocumentJsonReader documents,
    IWitInterfaceCatalogBuilder catalogs,
    IPreview2ShimIdentityReader shims,
    IHostingPlatformCatalogProjector projector,
    IHostingPlatformCatalogJavaScriptWriter writer) :
    IHostingPlatformCatalogApplication
{
    private const string PlatformWorld = "netwasm:hosting-platform@1.0.0/preview2";
    private readonly ITextFileStore _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IWitDocumentJsonReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitInterfaceCatalogBuilder _catalogs = catalogs ??
        throw new ArgumentNullException(nameof(catalogs));
    private readonly IPreview2ShimIdentityReader _shims = shims ??
        throw new ArgumentNullException(nameof(shims));
    private readonly IHostingPlatformCatalogProjector _projector = projector ??
        throw new ArgumentNullException(nameof(projector));
    private readonly IHostingPlatformCatalogJavaScriptWriter _writer = writer ??
        throw new ArgumentNullException(nameof(writer));

    public int Run(string[] arguments, TextWriter standardOutput, TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        if (arguments.Length != 3 || arguments.Any(string.IsNullOrWhiteSpace))
        {
            standardError.WriteLine(
                "usage: NetWasm.Hosting.Generator <normalized-wit-json> <preview2-shim-package-json> <output-esm>");
            return 2;
        }

        try
        {
            var document = _documents.Read(_files.Read(arguments[0]));
            var world = document.SelectWorld(PlatformWorld);
            var catalog = _catalogs.Build(document, world);
            var shim = _shims.Read(_files.Read(arguments[1]));
            var projected = _projector.Project(catalog, shim);
            _files.Write(arguments[2], _writer.Write(projected));
            standardOutput.WriteLine(
                $"Generated {projected.Providers.Length} platform providers from " +
                $"{catalog.Interfaces.Length} imported interfaces for {catalog.World}.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or Compiler.Core.CompilerException)
        {
            standardError.WriteLine(exception.Message);
            return 1;
        }
    }
}
