using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeArchiveValidationArgumentBuilder :
    IRuntimeNativeArchiveValidationArgumentBuilder
{
    public ImmutableArray<string> Build(RuntimeNativeArchiveValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var machine = request.Target switch
        {
            "wasm32" => "-mwasm32",
            "wasm64" => "-mwasm64",
            _ => throw new InvalidOperationException("The native library target is unsupported."),
        };
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!Path.IsPathFullyQualified(request.ArchivePath) ||
            !Path.IsPathFullyQualified(request.OutputPath) ||
            HasLineControl(request.ArchivePath) || HasLineControl(request.OutputPath))
            throw new InvalidOperationException("Native archive validation paths must be unambiguous absolute paths.");
        var archive = Path.GetFullPath(request.ArchivePath);
        var output = Path.GetFullPath(request.OutputPath);
        if (string.Equals(archive, output, StringComparison.Ordinal))
            throw new InvalidOperationException("Native archive validation cannot overwrite its input.");
        // Every member must have the right format/target. Alternative members need
        // not coexist in an ordinary lazy link, so duplicate definitions are allowed
        // only in this discarded validation output, never in the production link.
        return [machine, "-r", "--allow-multiple-definition", "--whole-archive", archive, "--no-whole-archive",
            "--allow-undefined", "-o", output];
    }

    private static bool HasLineControl(string value) => value.IndexOfAny(['\0', '\r', '\n']) >= 0;
}
