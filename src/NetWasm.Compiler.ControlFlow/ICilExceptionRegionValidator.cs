using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public interface ICilExceptionRegionValidator
{
    void Validate(CilMethodBody body, int methodEnd);
}
