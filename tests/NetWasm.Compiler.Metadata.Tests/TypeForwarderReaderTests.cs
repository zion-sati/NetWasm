using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class TypeForwarderReaderTests
{
    [Fact]
    public void ReadsChainedAndNestedForwardersWithoutUnifyingUnrelatedTypes()
    {
        var reader = Create(
            ("Facade", Image("Facade", new Export("Example", "Outer`1", "Middle"),
                new Export("", "Inner", "", Parent: 1))),
            ("Middle", Image("Middle", new Export("Example", "Outer`1", "Implementation"),
                new Export("", "Inner", "", Parent: 1))),
            ("Unrelated", Image("Unrelated")));

        var result = ((ITypeForwarderReader)reader).Read(["Facade", "Middle", "Unrelated"]);

        Assert.Equal(4, result.Count);
        Assert.Equal(new AssemblyIdentity("Implementation"), result[(new("Facade"), "Example.Outer`1")]);
        Assert.Equal(new AssemblyIdentity("Implementation"), result[(new("Facade"), "Example.Outer`1+Inner")]);
        Assert.False(result.ContainsKey((new("Unrelated"), "Example.Outer`1")));
        var aliases = new AssemblyIdentityAliases(ImmutableDictionary<string, string>.Empty, result);
        Assert.Equal(new AssemblyIdentity("Implementation"), aliases.Canonicalize(new("Facade"), "Example.Outer`1"));
        Assert.Equal(new AssemblyIdentity("Facade"), aliases.Canonicalize(new("Facade"), "Example.Missing"));
    }

    [Fact]
    public void RejectsCyclicAssemblyForwarding()
    {
        var reader = Create(("A", Image("A", new Export("", "Value", "B"))),
            ("B", Image("B", new Export("", "Value", "A"))));
        var error = Assert.Throws<CompilerException>(() => ((ITypeForwarderReader)reader).Read(["A", "B"]));
        Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
        Assert.Contains("cyclic type forwarding", error.Diagnostic.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsDuplicateAndCyclicNestedForwarders(bool identical)
    {
        var duplicate = Create(("A", Image("A", new Export("", "Value", "B"), new Export("", "Value", identical ? "B" : "C"))));
        Assert.Contains("duplicate", Assert.Throws<CompilerException>(() => duplicate.Read(["A"])).Diagnostic.Message);
        var nested = Create(("A", Image("A", new Export("", "Value", "", Parent: 1))));
        Assert.Contains("cyclic nested", Assert.Throws<CompilerException>(() => nested.Read(["A"])).Diagnostic.Message);
    }

    [Fact]
    public void DefersMissingDestinationsAndIgnoresNonForwardedModuleExports()
    {
        var reader = Create(("A", Image("A", new Export("", "Value", "Missing"),
            new Export("", "Other", "", File: true, Forwarder: false))));
        var map = ((ITypeForwarderReader)reader).Read(["A"]);
        Assert.Single(map);
        Assert.Equal(new AssemblyIdentity("Missing"), map[(new("A"), "Value")]);
    }

    [Fact]
    public void RejectsInvalidForwarderTargetsAndNullInputs()
    {
        var reader = Create(("A", Image("A", new Export("", "Value", "", File: true))));
        Assert.Contains("requires an assembly", Assert.Throws<CompilerException>(() => ((ITypeForwarderReader)reader).Read(["A"])).Diagnostic.Message);
        Assert.Throws<ArgumentNullException>(() => ((ITypeForwarderReader)reader).Read(null!));
        Assert.Throws<ArgumentNullException>(() => new TypeForwarderReader(null!));
        Assert.Empty(Create().Read([]));
    }

    [Fact]
    public void RejectsMalformedImagesWithACompilerDiagnostic()
    {
        var reader = Assert.IsAssignableFrom<ITypeForwarderReader>(Create(("invalid.dll", [1, 2, 3])));
        var error = Assert.Throws<CompilerException>(() => reader.Read(["invalid.dll"]));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
        Assert.Contains("invalid.dll", error.Diagnostic.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceAndRuntimeForwardersMayConvergeThroughDifferentScopes(bool reverse)
    {
        var reader = Create(
            ("runtime", Image("Facade", new Export("Example", "Value", "Middle"))),
            ("view", Image("Facade", new Export("Example", "Value", "Platform"))),
            ("middle", Image("Middle", new Export("Example", "Value", "Platform"))));
        var result = reader.Read(reverse ? ["view", "middle", "runtime"] : ["runtime", "middle", "view"]);
        Assert.Equal(new AssemblyIdentity("Platform"), result[(new("Facade"), "Example.Value")]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void RepeatedIdenticalForwardersAcrossImagesAreConsistent()
    {
        var reader = Create(
            ("runtime", Image("Facade", new Export("Example", "Value", "Platform"))),
            ("view", Image("Facade", new Export("Example", "Value", "Platform"))));
        Assert.Equal(new AssemblyIdentity("Platform"), Assert.Single(reader.Read(["runtime", "view"])).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsConflictingOrCyclicAlternativeForwarders(bool cycle)
    {
        var reader = Create(
            ("runtime", Image("Facade", new Export("Example", "Value", "Middle"))),
            ("view", Image("Facade", new Export("Example", "Value", "Platform"))),
            ("middle", Image("Middle", new Export("Example", "Value", cycle ? "Facade" : "Other"))));
        var error = Assert.Throws<CompilerException>(() => reader.Read(["runtime", "view", "middle"]));
        Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
        Assert.Contains(cycle ? "cyclic" : "conflicting", error.Diagnostic.Message);
    }

    [Fact]
    public void TerminalConvergenceUsesOnlyTheSuppliedAssemblyAliasPolicy()
    {
        var reader = Create(
            ("runtime", Image("Facade", new Export("Example", "Value", "Legacy"))),
            ("view", Image("Facade", new Export("Example", "Value", "Platform"))));
        Assert.Throws<CompilerException>(() => reader.Read(["runtime", "view"]));
        var aliases = ImmutableDictionary<string, string>.Empty.Add("Legacy", "Platform");
        var map = reader.Read(["runtime", "view"], aliases);
        Assert.Equal(new AssemblyIdentity("Platform"), Assert.Single(map).Value);
    }

    [Fact]
    public void ExplicitMetadataViewReplacesRuntimeFacadeBinding()
    {
        var reader = Create(
            ("runtime", Image("Facade", new Export("Example", "Value", "Legacy"))),
            ("view", Image("Facade", new Export("Example", "Value", "Platform"))));
        var map = reader.Read(["runtime"], metadataReferencePaths: ["view"]);
        Assert.Equal(new AssemblyIdentity("Platform"), Assert.Single(map).Value);
    }

    [Fact]
    public void MetadataOverlayDoesNotHideMalformedRuntimeForwardingOrCompetingViews()
    {
        var cyclic = Create(
            ("runtime", Image("Facade", new Export("Example", "Value", "Facade"))),
            ("view", Image("Facade", new Export("Example", "Value", "Platform"))));
        Assert.Throws<CompilerException>(() => cyclic.Read(["runtime"], metadataReferencePaths: ["view"]));
        var competing = Create(
            ("first", Image("Facade", new Export("Example", "Value", "First"))),
            ("second", Image("Facade", new Export("Example", "Value", "Second"))));
        Assert.Throws<CompilerException>(() => competing.Read([], metadataReferencePaths: ["first", "second"]));
    }

    private static TypeForwarderReader Create(params (string Path, byte[] Image)[] images) =>
        new TypeForwarderReader(new Images(images.ToDictionary(item => item.Path, item => item.Image)));

    private sealed class Images(Dictionary<string, byte[]> images) : IManagedAssemblyImageReader
    {
        public byte[] Read(string path) => images[path];
    }

    private sealed record Export(string Namespace, string Name, string Target,
        int Parent = 0, bool File = false, bool Forwarder = true);

    private static byte[] Image(string name, params Export[] exports)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        foreach (var export in exports)
        {
            EntityHandle implementation = export.Parent != 0
                ? MetadataTokens.ExportedTypeHandle(export.Parent)
                : export.File ? metadata.AddAssemblyFile(metadata.GetOrAddString("other.netmodule"), default, true)
                : metadata.AddAssemblyReference(metadata.GetOrAddString(export.Target), new(1, 0), default, default, 0, default);
            metadata.AddExportedType(TypeAttributes.Public | (export.Forwarder && export.Parent == 0 ? (TypeAttributes)0x00200000 : 0),
                metadata.GetOrAddString(export.Namespace), metadata.GetOrAddString(export.Name), implementation, 0);
        }
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
    }
}
