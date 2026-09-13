using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Functions;

public interface IWitCanonicalFunctionBuilder
{
    CanonicalAbiFunction Build(WitDocument document, string interfaceName, WitFunction witFunction);
}

public sealed class WitCanonicalFunctionBuilder(IWitCanonicalTypeResolver types) : IWitCanonicalFunctionBuilder
{
    private static readonly EntityKey GeneratedMethod = new(new AssemblyIdentity("generated"), 0);
    private readonly IWitCanonicalTypeResolver _types = types ?? throw new ArgumentNullException(nameof(types));

    public CanonicalAbiFunction Build(WitDocument document, string interfaceName, WitFunction witFunction)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(interfaceName);
        ArgumentNullException.ThrowIfNull(witFunction);
        return new(interfaceName, witFunction.Name, GeneratedMethod,
            [.. witFunction.Parameters.Select(parameter => new CanonicalAbiParameter(
                parameter.Name, _types.Resolve(document, parameter.Type)))],
            witFunction.Result is null ? null : _types.Resolve(document, witFunction.Result));
    }
}
