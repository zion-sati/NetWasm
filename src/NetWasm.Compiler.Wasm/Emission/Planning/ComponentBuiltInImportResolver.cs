using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// A Component Model built-in keeps its full core name, which wit-component
// classifies on its own, and its extern's core values, which wit-component
// checks against the built-in's canonical type when it encodes the component.
internal sealed class ComponentBuiltInImportResolver : IComponentBuiltInImportResolver
{
    public WasmFunctionImport Resolve(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.WitImport is not { IsCanonicalBuiltIn: true } builtIn)
        {
            throw new ArgumentException(
                "only a Component Model built-in resolves to a built-in import",
                nameof(method));
        }

        return new WasmFunctionImport(
            builtIn.InterfaceName,
            builtIn.FunctionName,
            WasmFunctionType.Create(
                method.Signature.ReturnType,
                [.. method.Signature.ParameterTypes]));
    }
}
