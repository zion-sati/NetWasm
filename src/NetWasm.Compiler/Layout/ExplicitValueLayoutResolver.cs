using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ExplicitValueLayoutResolver : IExplicitValueLayoutResolver
{
    public ValueLayout Resolve(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        ImmutableArray<FieldDefinitionModel> instanceFields)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.LayoutKind != CliTypeLayoutKind.Explicit)
        {
            throw new ArgumentException(
                "The type does not use explicit layout.",
                nameof(definition));
        }
        if (!instanceFields.IsEmpty)
        {
            throw Unsupported(type, "explicit field layout");
        }
        if (definition.DeclaredSize <= 0)
        {
            throw Unsupported(type, "explicit layout without a declared size");
        }

        return new ValueLayout(type, definition.DeclaredSize, 1, []);
    }

    private static CompilerException Unsupported(
        CliTypeIdentity type,
        string reason) => new(new CompilerDiagnostic(
        DiagnosticCode.UnsupportedMetadata,
        $"unsupported layout for '{type}': {reason}"));
}
