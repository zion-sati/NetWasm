using System;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class MethodValueLayoutResolver(
    ReachableProgram program,
    IValueLayoutResolver valueLayouts) : IMethodValueLayoutResolver
{
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly IValueLayoutResolver _valueLayouts = valueLayouts ??
        throw new ArgumentNullException(nameof(valueLayouts));

    public void Resolve()
    {
        foreach (var method in _program.Methods.Values)
            ResolveMethod(method);
        foreach (var method in _program.ConstructedMethods.Values)
            ResolveMethod(method);
    }

    private void ResolveMethod(ManagedMethodBody method)
    {
        var body = method.Body;
        foreach (var type in body.LocalSignatureTypes)
            ResolveClosedValueLayout(type);
        var signature = method.Method.Signature;
        foreach (var type in signature.ParameterSignatureTypes)
            ResolveClosedValueLayout(type);
        var returnType = signature.ReturnSignatureType;
        if (returnType.HasRuntimeStorage)
            ResolveClosedValueLayout(returnType);
    }

    private void ResolveClosedValueLayout(CliTypeIdentity type)
    {
        if (!type.ContainsGenericParameters)
            _valueLayouts.Resolve(type);
    }
}
