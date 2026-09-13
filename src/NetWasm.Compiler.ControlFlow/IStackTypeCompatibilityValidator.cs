using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public interface IStackTypeCompatibilityValidator
{
    bool Accepts(CliValueKind expected, CliValueKind actual);
}
