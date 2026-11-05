using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

/// <summary>Decides which methods are not structured before planning.</summary>
internal interface IMethodOutlineProvider
{
    // An outline to stand in for the method's structured form, or null to structure it now.
    StructuredMethod? Outline(ManagedMethodBody method);
}
