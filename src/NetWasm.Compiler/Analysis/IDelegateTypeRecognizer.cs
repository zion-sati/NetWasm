using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IDelegateTypeRecognizer
{
    bool Recognize(CliTypeIdentity type);
}
