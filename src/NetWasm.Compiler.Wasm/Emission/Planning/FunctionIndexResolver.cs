using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IFunctionIndexResolverFactory
{
    IFunctionIndexResolver Create(
        IMethodRepository methods,
        ISymbolFormatter symbols,
        FunctionIndexMap indices);
}

internal sealed class FunctionIndexResolverFactory : IFunctionIndexResolverFactory
{
    public IFunctionIndexResolver Create(
        IMethodRepository methods,
        ISymbolFormatter symbols,
        FunctionIndexMap indices) => new FunctionIndexResolver(methods, symbols, indices);
}

internal sealed class FunctionIndexResolver(
    IMethodRepository methods,
    ISymbolFormatter symbols,
    FunctionIndexMap indices) : IFunctionIndexResolver
{
    public int Resolve(EntityKey method)
    {
        if (indices.TryGetMethod(method, out var index))
        {
            return index.Value;
        }

        throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"reachable method '{symbols.Format(methods.GetMethod(method))}' " +
            $"({method}) has no Wasm function index"));
    }

    public int Resolve(string method)
    {
        if (indices.TryGetConstructedMethod(method, out var index))
        {
            return index.Value;
        }

        throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"reachable method '{method}' has no Wasm function index"));
    }

    public int Resolve(MethodInstanceModel method) =>
        method.IsConstructed
            ? Resolve(method.CanonicalName)
            : Resolve(method.Definition.Key);
}
