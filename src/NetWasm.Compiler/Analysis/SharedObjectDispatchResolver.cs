using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// The shared body (CoreLib's SharedObjectDispatch) for a virtual call on object's Equals, GetHashCode or
// ToString; null for any other declaration.
internal interface ISharedObjectDispatchResolver
{
    MethodInstanceModel? Resolve(MethodInstanceModel declaration);
}

// The speed presets dispatch every call on object inline (ObjectDispatchPolicy.Inline).
internal sealed class InlineObjectDispatchResolver : ISharedObjectDispatchResolver
{
    public MethodInstanceModel? Resolve(MethodInstanceModel declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        return null;
    }
}

internal sealed class SharedObjectDispatchResolver(
    ITypeFinder typeFinder,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : ISharedObjectDispatchResolver
{
    private const string HelperType = "System.Runtime.CompilerServices.SharedObjectDispatch";

    private readonly ITypeFinder _typeFinder = typeFinder ?? throw new ArgumentNullException(nameof(typeFinder));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IMethodInstanceResolver _methodInstances = methodInstances ??
        throw new ArgumentNullException(nameof(methodInstances));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));

    public MethodInstanceModel? Resolve(MethodInstanceModel declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var definition = declaration.Definition;
        if (definition.IsStatic || declaration.DeclaringType.CanonicalName != "primitive:object")
        {
            return null;
        }
        var helperName = (definition.Name, definition.Signature.ParameterTypes.Length) switch
        {
            ("Equals", 1) => "DispatchEquals",
            ("GetHashCode", 0) => "DispatchGetHashCode",
            ("ToString", 0) => "DispatchToString",
            _ => null,
        };
        if (helperName is null)
        {
            return null;
        }
        var candidates = _typeFinder.FindType(HelperType).Methods
            .Select(_methods.GetMethod)
            .Where(static method => method.IsStatic)
            .Where(method => method.Name == helperName)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"shared object dispatch must define exactly one '{helperName}'"));
        }
        var helper = candidates[0];
        return _methodInstances.ResolveMethodInstance(
            helper.Key.Assembly, helper.Key.MetadataToken, _symbols.Format(helper), 0);
    }
}
