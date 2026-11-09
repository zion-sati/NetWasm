using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.PlatformServices;

// Creates a compilation's platform resolver over its metadata services.
internal sealed class PlatformServiceBindingResolverFactory(
    IAssemblyAttributeDescriptorReaderFactory attributes) : IPlatformServiceBindingResolverFactory
{
    private readonly IAssemblyAttributeDescriptorReaderFactory _attributes = attributes ??
        throw new ArgumentNullException(nameof(attributes));

    public IPlatformServiceBindingResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IBaseTypeResolver baseTypes) =>
        new PlatformServiceBindingResolver(
            _attributes, types, typeDefinitions, identities, methods, methodInstances, symbols, baseTypes);
}

// Reads the program's NetWasmPlatformAttribute from every assembly but CoreLib, so a host's package
// can declare its platform, and resolves the platform to its parameterless constructor and its
// description (DeclaredPlatform). The application's own
// declaration takes precedence over a package's, so an application can replace or extend it.
internal sealed class PlatformServiceBindingResolver : IPlatformServiceBindingResolver
{
    internal const string Namespace = "System.Runtime.InteropServices.WebAssembly";
    internal const string AttributeName = Namespace + ".NetWasmPlatformAttribute";
    internal const string PlatformBase = Namespace + ".NetWasmPlatform";

    private readonly IAssemblyAttributeDescriptorReaderFactory _attributes;
    private readonly ITypeFinder _types;
    private readonly ITypeDefinitionResolver _typeDefinitions;
    private readonly ITypeIdentityResolver _identities;
    private readonly IMethodRepository _methods;
    private readonly IMethodInstanceResolver _methodInstances;
    private readonly ISymbolFormatter _symbols;
    private readonly IBaseTypeResolver _baseTypes;

    internal PlatformServiceBindingResolver(
        IAssemblyAttributeDescriptorReaderFactory attributes,
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IBaseTypeResolver baseTypes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(methodInstances);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(baseTypes);
        _attributes = attributes;
        _types = types;
        _typeDefinitions = typeDefinitions;
        _identities = identities;
        _methods = methods;
        _methodInstances = methodInstances;
        _symbols = symbols;
        _baseTypes = baseTypes;
    }

    // wasi: the program's world provides WASI, so its timers run on the Component Model's loop.
    public DeclaredPlatform? Resolve(MetadataCompilationSnapshot metadata, bool wasi)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var coreLib = _types.FindType("System.Object").Key.Assembly;
        var descriptors = _attributes.Create(metadata, _typeDefinitions, _identities);
        var values = new CustomAttributeValueDecoderFactory().Create(
            metadata, _typeDefinitions, _identities, _methodInstances, _types.FindType("System.Type").Key);
        var declarations = metadata.Assemblies
            .Where(assembly => assembly.Identity != coreLib)
            .SelectMany(assembly => descriptors.Read(assembly.Identity)
                .Where(descriptor => _typeDefinitions.ResolveTypeIdentity(descriptor.AttributeType).FullName == AttributeName)
                .Select(descriptor => (Identity: assembly.Identity, Descriptor: descriptor)))
            .ToArray();
        if (declarations.Length == 0)
        {
            return null;
        }

        var application = declarations.Where(declaration => declaration.Identity == metadata.EntryAssemblyIdentity).ToArray();
        if (application.Length == 0 && declarations.Length > 1)
        {
            throw Invalid("a program declares one platform, but " +
                string.Join(" and ", declarations.Select(declaration => $"'{declaration.Identity.Name}'")) +
                " declare one each; declare the platform in the application to choose");
        }

        var (identity, descriptor) = application.Length != 0 ? application[0] : declarations[0];
        var assemblyName = identity.Name;
        if (values.Decode(descriptor).FixedArguments is not [{ Value: CliTypeIdentity platform }])
        {
            throw Invalid($"the NetWasmPlatformAttribute in '{assemblyName}' must name a platform type");
        }

        return new DeclaredPlatform(
            Constructor(platform, wasi),
            $"'{_typeDefinitions.ResolveTypeIdentity(platform).FullName}', declared in {assemblyName}");
    }

    private MethodInstanceModel Constructor(CliTypeIdentity platform, bool wasi)
    {
        var definition = _typeDefinitions.ResolveTypeIdentity(platform);
        if (definition.IsInterface || definition.IsAbstract || definition.IsValueType || definition.GenericArity != 0)
        {
            throw Invalid($"'{definition.FullName}' must be a non-generic, non-abstract class");
        }

        var providesTimers = false;
        var derives = false;
        for (CliTypeIdentity? current = platform; current is not null; current = _baseTypes.Resolve(current))
        {
            var type = _typeDefinitions.ResolveTypeIdentity(current);
            if (type.FullName == PlatformBase)
            {
                derives = true;
                break;
            }

            providesTimers |= type.Methods.Select(_methods.GetMethod).Any(method => method.Name == "get_Timers");
        }

        if (!derives)
        {
            throw Invalid($"'{definition.FullName}' does not derive from NetWasmPlatform");
        }

        if (wasi && providesTimers)
        {
            throw Invalid($"'{definition.FullName}' provides Timers, but a program with WASI runs its timers on the Component Model's event loop");
        }

        var constructor = definition.Methods
            .Select(_methods.GetMethod)
            .SingleOrDefault(method =>
                method.Name == ".ctor" && method.IsPublic && method.Signature.ParameterTypes.IsEmpty)
            ?? throw Invalid($"'{definition.FullName}' has no public parameterless constructor");
        return _methodInstances.ResolveMethodInstance(
            constructor.Key.Assembly,
            constructor.Key.MetadataToken,
            _symbols.Format(constructor),
            0,
            new CliGenericContext([], []));
    }

    private static CompilerException Invalid(string reason) =>
        new(new CompilerDiagnostic(DiagnosticCode.PlatformService, "platform: " + reason));
}

internal interface IPlatformServiceBindingResolver
{
    DeclaredPlatform? Resolve(MetadataCompilationSnapshot metadata, bool wasi);
}

internal interface IPlatformServiceBindingResolverFactory
{
    IPlatformServiceBindingResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMethodRepository methods,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        IBaseTypeResolver baseTypes);
}
