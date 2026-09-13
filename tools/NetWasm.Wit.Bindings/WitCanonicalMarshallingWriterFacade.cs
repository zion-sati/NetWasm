using System;

namespace NetWasm.Wit.Bindings;

public interface IWitCanonicalMarshallingWriter
{
    string Generate(WitDocument document, WitWorld world);
}

public sealed class WitCanonicalMarshallingWriter(
    IWitCanonicalMarshallingTypeSectionWriter types) :
    IWitCanonicalMarshallingWriter
{
    private readonly IWitCanonicalMarshallingTypeSectionWriter _types = types ??
        throw new ArgumentNullException(nameof(types));

    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        return _types.Generate(document, world);
    }
}
