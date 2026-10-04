using System;
using System.IO;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleLinkInputValidator
{
    void Validate(RawModuleLinkRequest request);
}

public sealed class RawModuleLinkInputValidator(
    IComponentCoreModuleInputValidator inputs) : IRawModuleLinkInputValidator
{
    private readonly IComponentCoreModuleInputValidator _inputs = inputs ??
        throw new ArgumentNullException(nameof(inputs));

    public void Validate(RawModuleLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApplicationModulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeModulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (InvalidExports(request.InternalRuntimeExports) ||
            InvalidExports(request.InternalApplicationExports) ||
            request.InternalRuntimeExports.Select(export => export.Name)
                .Intersect(
                    request.InternalApplicationExports.Select(export => export.Name),
                    StringComparer.Ordinal)
                .Any())
            throw ComponentException.Invalid("raw linking requires complete and unique internal export facts");
        if (request.Target.Width is not ("wasm32" or "wasm64") ||
            request.Target.WasiVersion != "0.2" ||
            request.Target.CanonicalStringEncoding != "utf8")
        {
            throw ComponentException.Invalid("raw linking requires a Preview 2 UTF-8 wasm32 or wasm64 target");
        }

        var application = Path.GetFullPath(request.ApplicationModulePath);
        var runtime = Path.GetFullPath(request.RuntimeModulePath);
        var output = Path.GetFullPath(request.OutputPath);
        if (string.Equals(application, runtime, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(application, output, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(runtime, output, StringComparison.OrdinalIgnoreCase))
        {
            throw ComponentException.Invalid("raw linking requires distinct application, runtime and output paths");
        }
        _inputs.Validate(new ComponentCoreModuleLinkRequest(
            request.ApplicationModulePath,
            request.RuntimeModulePath,
            request.OutputPath,
            request.Target));
    }

    private static bool InvalidExports(
        System.Collections.Immutable.ImmutableArray<WasmInternalExport> exports) =>
        exports.IsDefault ||
        exports.Any(export =>
            export is null ||
            string.IsNullOrWhiteSpace(export.Name) ||
            export.Name.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            export.Kind is not (0 or 3)) ||
        exports.Select(export => export.Name)
            .Distinct(StringComparer.Ordinal).Count() != exports.Length;
}
