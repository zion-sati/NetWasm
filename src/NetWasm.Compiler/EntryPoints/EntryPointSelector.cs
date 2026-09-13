using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface IEntryPointSelector
{
    MethodDefinitionModel Select(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        ISymbolFormatter symbols,
        CompilerOptions options);
}

internal sealed class EntryPointSelector(
    IEntryPointValidator validator) : IEntryPointSelector
{
    private readonly IEntryPointValidator _validator = validator ??
        throw new ArgumentNullException(nameof(validator));

    public MethodDefinitionModel Select(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        ISymbolFormatter symbols,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(options);
        var entryPoint = options.EntryMethodToken is { } metadataToken
            ? methods.FindMethod(metadata.EntryAssemblyIdentity, metadataToken)
            : methods.FindMethod(
                metadata.EntryAssemblyIdentity,
                options.EntryTypeName,
                options.EntryMethodName);
        _validator.Validate(entryPoint, symbols, options.EntryPointKind);
        return entryPoint;
    }
}
