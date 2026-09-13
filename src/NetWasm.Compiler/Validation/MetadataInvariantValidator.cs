using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Validation;

internal sealed class MetadataInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions) : IMetadataInvariantValidator
{
    public void Validate(
        MetadataCompilationSnapshot metadata,
        ITypeRepository types,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(symbols);
        RequireUnique(
            metadata.Types.Select(type => type.Key),
            "type definition identity");
        RequireUnique(
            metadata.Methods.Select(method => method.Key),
            "method definition identity");
        foreach (var method in metadata.Methods)
        {
            _ = types.GetTypeDefinition(method.DeclaringType);
        }
        foreach (var method in metadata.EntryAssemblyMethods)
        {
            if (method.Key.Assembly != metadata.EntryAssemblyIdentity)
            {
                throw exceptions.Create(
                    "entry-assembly method has a foreign assembly identity",
                    symbols.Format(method));
            }
        }
    }

    private void RequireUnique<T>(IEnumerable<T> values, string kind)
        where T : notnull
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw exceptions.Create(
                    $"duplicate {kind} '{value}'");
            }
        }
    }
}
