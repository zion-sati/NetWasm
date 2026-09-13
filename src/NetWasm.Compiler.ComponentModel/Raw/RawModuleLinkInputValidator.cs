using System;
using System.IO;

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
}
