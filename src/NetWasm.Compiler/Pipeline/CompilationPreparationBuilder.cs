using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationPreparationBuilder(
    IBaseTypeResolverFactory baseTypes,
    IJavaScriptAsyncBindingResolverFactory asyncBindings,
    IComponentContractResolver contracts,
    IInteropDeclarationValidator interop,
    ICompilationEntryResolver entries,
    IComponentExportMerger exports,
    ITypeFinderFactory typeFinders,
    ITypeDefinitionResolverFactory typeDefinitions,
    ITypeIdentityResolverFactory identities,
    IBaseTypeIdentityResolverFactory baseTypeIdentities,
    IFieldRepositoryFactory fields,
    IMethodRepositoryFactory methods,
    IMethodInstanceResolverFactory methodInstances,
    IMethodFinderFactory methodFinders,
    ISymbolFormatterFactory symbols,
    IStackTraceReachabilityRootProvider stackTraceRoots,
    IManagedExecutableArgumentFactoryResolver argumentFactories) :
    ICompilationPreparationBuilder
{
    private readonly IBaseTypeResolverFactory _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly IJavaScriptAsyncBindingResolverFactory _asyncBindings = asyncBindings ??
        throw new ArgumentNullException(nameof(asyncBindings));
    private readonly IComponentContractResolver _contracts = contracts ??
        throw new ArgumentNullException(nameof(contracts));
    private readonly IInteropDeclarationValidator _interop = interop ??
        throw new ArgumentNullException(nameof(interop));
    private readonly ICompilationEntryResolver _entries = entries ??
        throw new ArgumentNullException(nameof(entries));
    private readonly IComponentExportMerger _exports = exports ??
        throw new ArgumentNullException(nameof(exports));
    private readonly ITypeFinderFactory _typeFinders = typeFinders ??
        throw new ArgumentNullException(nameof(typeFinders));
    private readonly ITypeDefinitionResolverFactory _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolverFactory _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly IBaseTypeIdentityResolverFactory _baseTypeIdentities = baseTypeIdentities ??
        throw new ArgumentNullException(nameof(baseTypeIdentities));
    private readonly IFieldRepositoryFactory _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepositoryFactory _methods = methods ??
        throw new ArgumentNullException(nameof(methods));
    private readonly IMethodInstanceResolverFactory _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly IMethodFinderFactory _methodFinders = methodFinders ??
        throw new ArgumentNullException(nameof(methodFinders));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly IStackTraceReachabilityRootProvider _stackTraceRoots =
        stackTraceRoots ?? throw new ArgumentNullException(nameof(stackTraceRoots));
    private readonly IManagedExecutableArgumentFactoryResolver _argumentFactories =
        argumentFactories ?? throw new ArgumentNullException(nameof(argumentFactories));

    public CompilationPreparation Prepare(
        MetadataCompilationSnapshot metadata,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        var types = _typeFinders.Create(metadata);
        var typeDefinitions = _typeDefinitions.Create(metadata);
        var identities = _identities.Create(metadata);
        var baseTypeIdentities = _baseTypeIdentities.Create(metadata);
        var fields = _fields.Create(metadata);
        var methods = _methods.Create(metadata);
        var methodInstances = _methodInstances.Create(metadata);
        var methodFinder = _methodFinders.Create(metadata);
        var symbols = _symbols.Create(metadata);

        var baseTypeResolver = _baseTypes.Create(types, identities, baseTypeIdentities);
        var asyncBindingResolver = _asyncBindings.Create(
            types,
            typeDefinitions,
            identities,
            fields,
            methods,
            methodInstances,
            symbols);
        var componentContract = _contracts.Resolve(metadata, symbols, options);
        _interop.Validate(metadata, typeDefinitions, baseTypeResolver, methods, symbols);
        var entry = _entries.Resolve(metadata, methodFinder, symbols, options);
        var exports = _exports.Merge(
            entry.Exports,
            componentContract,
            options.Target);
        var argumentFactory = _argumentFactories.Resolve(
            options.EntryPointKind,
            entry.EntryPoint,
            types,
            methods);
        var reachabilityRoots = _stackTraceRoots.Provide(
            options.EmitStackTrace,
            types,
            fields);
        if (argumentFactory is { } method)
        {
            reachabilityRoots = reachabilityRoots with
            {
                Methods = reachabilityRoots.Methods.Add(method),
            };
        }
        return new CompilationPreparation(
            componentContract,
            entry,
            exports,
            reachabilityRoots,
            argumentFactory);
    }
}
