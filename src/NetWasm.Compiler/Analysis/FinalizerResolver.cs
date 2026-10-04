using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class FinalizerResolver(
    ITypeDefinitionResolver types,
    IMethodRepository methods,
    IBaseTypeResolver baseTypes) : IFinalizerResolver
{
    public MethodInstanceModel? Resolve(CliTypeIdentity allocatedType)
    {
        for (var current = allocatedType;
             current is not null;
             current = baseTypes.Resolve(current))
        {
            var definition = types.ResolveTypeIdentity(current);
            if (definition.FullName == "System.Object")
            {
                return null;
            }

            var finalizer = definition.Methods
                .Select(methods.GetMethod)
                .SingleOrDefault(IsFinalizerOverride);
            if (finalizer is null)
            {
                continue;
            }

            var typeArguments = current.Shape == CliTypeShape.GenericInstantiation
                ? current.TypeArguments
                : ImmutableArray<CliTypeIdentity>.Empty;
            return new MethodInstanceModel(
                finalizer,
                current,
                [],
                finalizer.Signature.Substitute(typeArguments));
        }

        return null;
    }

    private static bool IsFinalizerOverride(MethodDefinitionModel method) =>
        method.Name == "Finalize" &&
        !method.IsStatic &&
        method.IsVirtual &&
        !method.IsNewSlot &&
        method.GenericArity == 0 &&
        method.Signature.ReturnType == CliValueKind.Void &&
        method.Signature.ParameterTypes.IsEmpty;
}
