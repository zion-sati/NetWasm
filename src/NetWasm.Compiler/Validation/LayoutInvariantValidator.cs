using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal sealed class LayoutInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions) : ILayoutInvariantValidator
{
    public void Validate(ITargetLayout layouts, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        if (layouts.Target.Target != target)
        {
            throw exceptions.Create(
                $"layout target {layouts.Target.Target} does not match requested {target}");
        }
    }
}
