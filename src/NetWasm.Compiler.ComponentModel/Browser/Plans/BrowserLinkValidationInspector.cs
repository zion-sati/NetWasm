using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkValidationInspector : IBrowserLinkOperationInspector
{
    private static readonly FrozenSet<string> Features = new[]
    {
        "mvp", "mutable-global", "saturating-float-to-int", "sign-extension", "reference-types",
        "multi-value", "bulk-memory", "exceptions", "multi-memory", "memory64", "simd",
    }.ToFrozenSet(StringComparer.Ordinal);

    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkValidationOperation validation || validation.Arguments.IsDefault ||
            validation.Arguments.Length != 4 || validation.Arguments[0] != "validate" ||
            validation.Arguments[1] != validation.Path || validation.Arguments[2] != "--features" ||
            string.IsNullOrWhiteSpace(validation.Arguments[3]))
            throw new ArgumentException("An explicit core validation invocation is required.", nameof(operation));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in validation.Arguments[3].Split(','))
        {
            if (!Features.Contains(feature) || !seen.Add(feature))
                throw new ArgumentException("The validation feature is unsupported or repeated.", nameof(operation));
        }
        return new("validate", [validation.Path], [], [], ValidatesInputs: true)
        {
            ToolId = "wasm-tools",
            Arguments = validation.Arguments,
            InputArgumentIndices = [1],
        };
    }
}
