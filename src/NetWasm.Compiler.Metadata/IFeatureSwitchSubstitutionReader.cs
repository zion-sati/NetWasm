using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

/// <summary>Reads the method stubs an assembly's embedded ILLink.Substitutions.xml declares, by method token.</summary>
internal interface IFeatureSwitchSubstitutionReader
{
    Dictionary<int, ImmutableArray<FeatureSwitchStub>> Read(PEReader image, MetadataReader reader);
}
