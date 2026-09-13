using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal interface ILayoutInvariantValidator
{
    void Validate(ITargetLayout layouts, WasmTarget target);
}
