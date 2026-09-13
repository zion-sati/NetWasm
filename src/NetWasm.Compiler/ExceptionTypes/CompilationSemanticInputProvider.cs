using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.ExceptionTypes;

public interface ICompilationSemanticInputProvider
{
    IEnumerable<string> GetSemanticInputs(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        IEnumerable<ReachableExceptionType> exceptionTypes);
}

public sealed class CompilationSemanticInputProvider(
    ICompilationInputHasher inputHasher) : ICompilationSemanticInputProvider
{
    private readonly ICompilationInputHasher _inputHasher = inputHasher ??
        throw new ArgumentNullException(nameof(inputHasher));

    public IEnumerable<string> GetSemanticInputs(
        CompilerOptions options,
        MetadataCompilationSnapshot metadata,
        IEnumerable<ReachableExceptionType> exceptionTypes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(exceptionTypes);

        var inputs = ImmutableArray.CreateBuilder<string>();
        inputs.Add("diagnostic-artifact-schema=1");
        inputs.Add("target=" + options.Target);
        inputs.Add("entry-assembly=" + metadata.EntryAssemblyIdentity.Name);
        inputs.Add("entry-type=" + options.EntryTypeName);
        inputs.Add("entry-method=" + options.EntryMethodName);
        inputs.Add("input=" + _inputHasher.Hash(options.EntryAssemblyPath));
        foreach (var reference in options.ReferencePaths
                     .Select(_inputHasher.Hash)
                     .Order(StringComparer.Ordinal))
            inputs.Add("reference=" + reference);
        foreach (var export in options.Exports.Select(export => export.ToString()).Order(StringComparer.Ordinal))
            inputs.Add("export=" + export);
        if (options.ReferenceAssemblyAliases is not null)
        {
            foreach (var alias in options.ReferenceAssemblyAliases.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                inputs.Add("alias=" + alias.Key + "=" + alias.Value);
        }
        if (options.WitPath is not null)
            inputs.Add("wit=" + _inputHasher.Hash(options.WitPath));
        if (options.WitWorld is not null)
            inputs.Add("wit-world=" + options.WitWorld);
        foreach (var exceptionType in exceptionTypes.OrderBy(type => type.TypeId))
            inputs.Add("exception=" + exceptionType.TypeId + ":" + exceptionType.CanonicalIdentity);
        return inputs.ToImmutable();
    }
}
