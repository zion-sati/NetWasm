using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationLayoutBuilder(
    IManagedLayoutCompiler compiler,
    ILayoutInvariantValidator invariants,
    ITypeRepositoryFactory typeRepositories,
    IFieldRepositoryFactory fieldRepositories,
    ITypeFinderFactory typeFinders,
    ITypeDefinitionResolverFactory typeDefinitions,
    ITypeIdentityResolverFactory identities,
    IMetadataEntityBaseTypeResolverFactory baseTypes,
    IMetadataIdentityBaseTypeResolverFactory identityBaseTypes) : ICompilationLayoutBuilder
{
    private readonly IManagedLayoutCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));
    private readonly ILayoutInvariantValidator _invariants = invariants ??
        throw new ArgumentNullException(nameof(invariants));
    private readonly ITypeRepositoryFactory _typeRepositories = typeRepositories ??
        throw new ArgumentNullException(nameof(typeRepositories));
    private readonly IFieldRepositoryFactory _fieldRepositories = fieldRepositories ??
        throw new ArgumentNullException(nameof(fieldRepositories));
    private readonly ITypeFinderFactory _typeFinders = typeFinders ??
        throw new ArgumentNullException(nameof(typeFinders));
    private readonly ITypeDefinitionResolverFactory _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolverFactory _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly IMetadataEntityBaseTypeResolverFactory _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly IMetadataIdentityBaseTypeResolverFactory _identityBaseTypes =
        identityBaseTypes ?? throw new ArgumentNullException(nameof(identityBaseTypes));

    public CompilationLayouts Compile(
        MetadataCompilationSnapshot metadata,
        ReachableProgram program,
        WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(program);

        var types = _typeRepositories.Create(metadata);
        var fields = _fieldRepositories.Create(metadata);
        var typeFinder = _typeFinders.Create(metadata);
        var typeDefinitions = _typeDefinitions.Create(metadata);
        var identities = _identities.Create(metadata);
        var baseTypes = _baseTypes.Create(metadata);
        var identityBaseTypes = _identityBaseTypes.Create(metadata);

        var snapshot = _compiler.Compile(
            metadata,
            types,
            fields,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypes,
            identityBaseTypes,
            program,
            target);
        _invariants.Validate(snapshot, target);
        return new CompilationLayouts(snapshot);
    }
}
