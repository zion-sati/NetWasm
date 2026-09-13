using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

internal static class WitCanonicalMarshallingWriterFixture
{
    public static IWitCanonicalMarshallingWriter Create()
    {
        return new WitCanonicalMarshallingWriter(
            new WitCanonicalMarshallingTypeSectionWriter(
                new WitCanonicalTypeResolver(),
                new CanonicalAbiMemoryLayoutPlanner(),
                new WitCanonicalTypeReachabilityResolver(),
                new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new CodeWriterFactory()));
    }
}
