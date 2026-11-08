using System;
using System.Linq;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

// A library whose code uses a Wasm feature outside the static-native profile,
// such as tail calls or threads, fails here with its name, the profile and the
// validator's reason, before the whole module's validation could only report
// that a tool failed.
internal sealed class RuntimeNativeArchiveFeatureValidator(
    IRuntimeNativeModuleValidator modules,
    IRuntimeArtifactReader artifacts) : IRuntimeNativeArchiveFeatureValidator
{
    private readonly IRuntimeNativeModuleValidator _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    private readonly IRuntimeArtifactReader _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    public void Validate(RuntimeNativeArchiveFeatureValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LibraryName);
        ArgumentNullException.ThrowIfNull(request.Module);
        try
        {
            _modules.Validate(request.Module);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Native library '{request.LibraryName}' uses WebAssembly beyond NetWasm's static native profile " +
                $"({string.Join(", ", request.Module.Profile.Features)}): {Reason(request.Module.LogPath)}. " +
                "Build it without that feature.", exception);
        }
    }

    // wasm-tools names the missing feature under "Caused by:", after the function that failed.
    private string Reason(string logPath)
    {
        var lines = Encoding.UTF8.GetString(_artifacts.Read(logPath))
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cause = lines.SkipWhile(line => line != "Caused by:").Skip(1).FirstOrDefault();
        if (cause is null) return lines.FirstOrDefault() ?? "the validator gave no reason";
        // The cause is numbered: "0: SIMD support is not enabled (at offset 0x103b)".
        var separator = cause.IndexOf(": ", StringComparison.Ordinal);
        return separator < 0 ? cause : cause[(separator + 2)..];
    }
}
