using System;
using System.Linq;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticLayoutTraceFormatter :
    ICompilerDiagnosticLayoutTraceFormatter
{
    public string FormatLayouts(
        ReachableProgram program,
        ManagedLayoutSnapshot layouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        ISymbolFormatter symbols,
        ITypeRepository types,
        IFieldRepository fields)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(typeLayouts);
        ArgumentNullException.ThrowIfNull(instanceFields);
        ArgumentNullException.ThrowIfNull(staticFields);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        var text = new StringBuilder();
        text.AppendLine("[layouts]");
        foreach (var descriptor in layouts.TypeDescriptors.OrderBy(item => item.TypeId))
        {
            var layout = typeLayouts.GetObjectLayout(descriptor.Type);
            text.Append("type ").Append(descriptor.TypeId).Append(' ')
                .Append(symbols.Format(descriptor.Type))
                .Append(" size=").Append(layout.Size)
                .Append(" refs=").AppendJoin(',', layout.ReferenceOffsets)
                .AppendLine();
        }
        foreach (var descriptor in layouts.ConstructedTypeDescriptors.OrderBy(
                     item => item.TypeId))
        {
            var layout = typeLayouts.GetObjectLayout(descriptor.Type);
            text.Append("type ").Append(descriptor.TypeId).Append(' ')
                .Append(descriptor.Type.CanonicalName)
                .Append(" size=").Append(layout.Size)
                .Append(" refs=").AppendJoin(',', layout.ReferenceOffsets)
                .AppendLine();
        }
        foreach (var fieldKey in program.Fields
                     .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                     .ThenBy(key => key.MetadataToken))
        {
            var field = fields.GetField(fieldKey);
            if (!field.IsStatic ||
                types.GetTypeDefinition(field.DeclaringType).GenericArity != 0)
            {
                continue;
            }
            text.Append("static-field ").Append(fieldKey)
                .Append(" address=")
                .Append(staticFields.GetStaticFieldLayout(fieldKey).Address)
                .Append(" type=").Append(field.SignatureType.CanonicalName)
                .AppendLine();
        }
        foreach (var field in program.ConstructedFields.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            var layout = field.Value.Definition.IsStatic
                ? staticFields.GetStaticFieldLayout(field.Value).Address
                : instanceFields.GetFieldLayout(field.Value).Offset;
            text.Append("field ").Append(field.Key).Append(" offset=")
                .Append(layout).AppendLine();
        }
        text.AppendLine();
        return text.ToString().ReplaceLineEndings("\n");
    }
}
