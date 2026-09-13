using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodBodyReader(
    ICilDecoderFactory decoders,
    IManagedAssemblyResolver assemblies) : IMethodBodyReader
{
    public CilMethodBody ReadMethodBody(MethodDefinitionModel method) =>
        decoders.Create().Decode(assemblies.Resolve(method.Key.Assembly), method);

    public CilMethodBody ReadMethodBody(MethodInstanceModel method) =>
        decoders.Create().Decode(assemblies.Resolve(method.Definition.Key.Assembly), method);
}
