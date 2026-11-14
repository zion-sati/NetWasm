using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Reads the body of a method other than the one being analyzed, for a rewrite or optimization
// that looks into a callee, constructor or initializer. A body that cannot be decoded (it
// references a member or assembly that is missing, say) is that method's own failure, which the
// compiler reports with the method's assembly and package when it analyzes the method. The
// look-ahead therefore treats such a body as unavailable and leaves its caller as it is.
public interface ICalleeMethodBodyReader
{
    bool TryReadMethodBody(MethodDefinitionModel method, [NotNullWhen(true)] out CilMethodBody? body);

    bool TryReadMethodBody(MethodInstanceModel method, [NotNullWhen(true)] out CilMethodBody? body);
}
