using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

public interface IRuntimeTypeAssemblyResolver
{
    AssemblyIdentity Resolve(CliTypeIdentity type);
}

public interface IRuntimeTypeAssemblyResolverFactory
{
    IRuntimeTypeAssemblyResolver Create(ITypeDefinitionResolver definitions);
}

// Factory: the assembly resolver over one compilation's type definitions.
internal sealed class RuntimeTypeAssemblyResolverFactory : IRuntimeTypeAssemblyResolverFactory
{
    public IRuntimeTypeAssemblyResolver Create(ITypeDefinitionResolver definitions) => new RuntimeTypeAssemblyResolver(definitions);
}

// Strategy: the assembly Type.Assembly reports, as .NET reports it: a type definition's own
// assembly (a forwarded type's real one), and for an array, pointer, by-reference or constructed
// type the assembly of its element type or generic definition; a function pointer type is CoreLib's.
// A generic parameter's assembly is its owner's, which only the owner's registration knows.
internal sealed class RuntimeTypeAssemblyResolver(ITypeDefinitionResolver definitions) : IRuntimeTypeAssemblyResolver
{
    private readonly ITypeDefinitionResolver _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    public AssemblyIdentity Resolve(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape switch
        {
            CliTypeShape.Primitive or CliTypeShape.Named => _definitions.ResolveTypeIdentity(type).Key.Assembly,
            CliTypeShape.GenericInstantiation or CliTypeShape.SzArray or CliTypeShape.Array or
                CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or CliTypeShape.Modified =>
                Resolve(type.ElementType!),
            CliTypeShape.FunctionPointer => Resolve(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)),
            _ => throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"the assembly of '{type}' is its owner's")),
        };
    }
}
