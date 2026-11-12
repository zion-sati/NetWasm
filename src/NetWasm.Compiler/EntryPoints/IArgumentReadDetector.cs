using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.EntryPoints;

internal interface IArgumentReadDetector
{
    bool Detect(CilMethodBody body, int argument);
}
