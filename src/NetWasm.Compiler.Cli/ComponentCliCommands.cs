using System;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal sealed class ComponentizeCliCommand(
    IWitDocumentReader documents,
    IWitWorldValidator validator,
    IComponentManifestBuilder manifests,
    IComponentPackager packager,
    IComponentManifestInputReader manifestInputs,
    ITextFileWriter textFiles) : ICompilerCliCommand
{
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitWorldValidator _validator = validator ??
        throw new ArgumentNullException(nameof(validator));
    private readonly IComponentManifestBuilder _manifests = manifests ??
        throw new ArgumentNullException(nameof(manifests));
    private readonly IComponentPackager _packager = packager ??
        throw new ArgumentNullException(nameof(packager));
    private readonly IComponentManifestInputReader _manifestInputs = manifestInputs ??
        throw new ArgumentNullException(nameof(manifestInputs));
    private readonly ITextFileWriter _textFiles = textFiles ??
        throw new ArgumentNullException(nameof(textFiles));

    public string Name => "componentize";

    public int Run(string[] arguments)
    {
        var options = ComponentizeCliOptions.Parse(arguments);
        var target = options.Target == WasmTarget.Wasm32
            ? ComponentTarget.Wasm32Wasi02
            : ComponentTarget.Wasm64Wasi02;
        var document = _documents.Read(options.Wit);
        var world = document.SelectWorld(options.World);
        _validator.Validate(document, world);
        var manifest = _manifests.Build(
            document,
            world,
            target,
            _manifestInputs.Read(options));
        _packager.Package(new ComponentPackageRequest(
            options.CoreModule,
            options.Wit,
            options.World,
            options.Output,
            target,
            options.RuntimeModule,
            Optimization: options.Optimization));
        _textFiles.Write(options.Manifest, CliJson.Serialize(manifest));
        return 0;
    }
}

internal interface IComponentManifestInputReader
{
    ComponentManifestInputs Read(ComponentizeCliOptions options);
}

internal sealed class ComponentManifestInputReader : IComponentManifestInputReader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITextFileReader _textFiles;

    public ComponentManifestInputReader(ITextFileReader textFiles)
    {
        _textFiles = textFiles ?? throw new ArgumentNullException(nameof(textFiles));
    }

    public ComponentManifestInputs Read(ComponentizeCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var interop = options.InteropManifest is null
            ? null
            : JsonSerializer.Deserialize<HostInteropManifest>(
                _textFiles.Read(options.InteropManifest),
                ReadOptions)
              ?? throw CliOptionException.Create(
                  $"interop manifest '{options.InteropManifest}' is empty");
        if (interop is not null)
        {
            var expectedTarget = options.Target == WasmTarget.Wasm64
                ? "wasm64"
                : "wasm32";
            if (interop.Target != expectedTarget)
            {
                throw CliOptionException.Create(
                    $"interop manifest target '{interop.Target}' does not match component target '{expectedTarget}'");
            }
        }
        return new ComponentManifestInputs(
            interop is null
                ? ComponentJavaScriptBoundary.Empty
                : new ComponentJavaScriptBoundary(
                    [.. interop.Imports.Select(import => new ComponentJavaScriptImport(
                        import.Module,
                        import.Name,
                        import.Parameters,
                        import.Result,
                        import.AsyncReturn))],
                    [.. interop.Exports.Select(export => new ComponentJavaScriptExport(
                        export.Name,
                        export.Parameters,
                        export.Result,
                        export.AsyncReturn))]),
            new ComponentAdapterVersions(
                options.JcoVersion,
                options.Preview2ShimVersion));
    }
}
