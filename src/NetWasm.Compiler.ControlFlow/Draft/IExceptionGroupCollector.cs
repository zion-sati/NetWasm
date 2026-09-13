using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IExceptionGroupCollector
{
    ImmutableArray<StructuredExceptionGroupDraft> Collect(StructuredMethodDraft method);
}
