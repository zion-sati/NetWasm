using System;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawWitFunctionLayoutBuilder
{
    RawWitFunctionLayout Build(WitDocument document, string interfaceName, WitFunction witFunction, WasmTarget target);
}

public sealed class RawWitFunctionLayoutBuilder(
    IWitCanonicalFunctionBuilder functions,
    IRawCanonicalFunctionLayoutPlanner layouts) : IRawWitFunctionLayoutBuilder
{
    private readonly IWitCanonicalFunctionBuilder _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    private readonly IRawCanonicalFunctionLayoutPlanner _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));

    public RawWitFunctionLayout Build(WitDocument document, string interfaceName, WitFunction witFunction, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(interfaceName);
        ArgumentNullException.ThrowIfNull(witFunction);
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        var canonical = _functions.Build(document, interfaceName, witFunction);
        return new(witFunction, _layouts.Plan(canonical, target));
    }
}
