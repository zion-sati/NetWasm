using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ExplicitValueLayoutResolver : IExplicitValueLayoutResolver
{
    public ExplicitValueLayoutPlan Resolve(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        ImmutableArray<ExplicitFieldStorage> instanceFields,
        WasmTargetLayout target)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(target);
        if (definition.LayoutKind != CliTypeLayoutKind.Explicit)
        {
            throw new ArgumentException(
                "The type does not use explicit layout.",
                nameof(definition));
        }
        if (definition.GenericArity != 0)
        {
            throw Unsupported(type, "generic explicit layout");
        }
        if (definition.InlineArrayLength != 0)
        {
            throw Unsupported(type, "explicit inline array layout");
        }
        if (definition.DeclaredSize < 0)
            throw Unsupported(type, "negative declared size");
        var fields = ImmutableArray.CreateBuilder<FieldLayout>();
        var references = new SortedSet<int>();
        var byReferences = new SortedSet<int>();
        var size = definition.DeclaredSize;
        var alignment = 1;
        foreach (var storage in instanceFields)
        {
            if (storage.Definition.ExplicitOffset is not { } offset || offset < 0)
                throw Unsupported(type, "missing or negative explicit field offset");
            if (storage.Layout.Size > int.MaxValue - offset)
                throw Unsupported(type, "explicit field extent exceeds supported address range");
            var fieldAlignment = definition.PackingSize == 0
                ? storage.Layout.Alignment
                : Math.Min(definition.PackingSize, storage.Layout.Alignment);
            if (storage.Layout.ContainsReferences)
                fieldAlignment = Math.Max(fieldAlignment, target.ObjectReferenceAlignment);
            alignment = Math.Max(alignment, fieldAlignment);
            size = Math.Max(size, offset + storage.Layout.Size);
            fields.Add(new FieldLayout(offset)
            {
                Type = storage.Layout.Type,
                Size = storage.Layout.Size,
            });
            foreach (var reference in storage.Layout.ReferenceOffsets)
            {
                var absolute = offset + reference;
                if (absolute % target.ObjectReferenceAlignment != 0)
                    throw Unsupported(type, "misaligned explicit reference slot");
                references.Add(absolute);
            }
            foreach (var byReference in storage.Layout.ByReferenceOffsets)
                byReferences.Add(offset + byReference);
        }
        foreach (var reference in references)
        {
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field.Offset < reference + target.ObjectReferenceSize &&
                    field.Offset + field.Size > reference &&
                    (!instanceFields[index].Layout.ReferenceOffsets.Contains(reference - field.Offset) ||
                     instanceFields[index].Layout.ByReferenceOffsets.Contains(reference - field.Offset) !=
                     byReferences.Contains(reference)))
                {
                    throw Unsupported(type, "explicit reference overlaps incompatible storage");
                }
            }
        }
        var roundSize = definition.DeclaredSize == 0 || references.Count != 0;
        if (roundSize && size > int.MaxValue - (alignment - 1))
            throw Unsupported(type, "explicit size exceeds supported address range");
        return new ExplicitValueLayoutPlan(
            new ValueLayout(type,
                roundSize ? ManagedTypeLayoutCompiler.Align(Math.Max(1, size), alignment) : size,
                alignment, [.. references]) { ByReferenceOffsets = [.. byReferences] },
            fields.ToImmutable());
    }

    private static CompilerException Unsupported(
        CliTypeIdentity type,
        string reason) => new(new CompilerDiagnostic(
        DiagnosticCode.UnsupportedMetadata,
        $"unsupported layout for '{type}': {reason}"));
}
