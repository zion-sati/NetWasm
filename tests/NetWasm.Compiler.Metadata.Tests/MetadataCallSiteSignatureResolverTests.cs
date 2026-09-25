using System.Collections.Immutable;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCallSiteSignatureResolverTests
{
    [Fact]
    public void ResolveRejectsTokenThatIsNotAStandaloneSignature()
    {
        var reader = (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader));
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            reader,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());

        Action<IMetadataCallSiteSignatureResolver> contract = resolver =>
            Assert.Throws<CompilerException>(() =>
                resolver.Resolve(source, 0, CliGenericContext.Empty));

        contract(new MetadataCallSiteSignatureResolver());
    }

    [Fact]
    public void ResolveReadsAStandaloneMethodSignatureThroughItsInterface()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "CallSiteSignature",
            """
            public static class EntryPoint
            {
                public static unsafe int Invoke(delegate* managed<int, int> target, int value) =>
                    target(value);

            }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var rowCount = assembly.Reader.GetTableRowCount(TableIndex.StandAloneSig);
        Assert.True(rowCount > 0);
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var resolver = new MetadataCallSiteSignatureResolver();
        MethodSignatureModel? signature = null;
        for (var row = 1; row <= rowCount && signature is null; row++)
        {
            try
            {
                signature = resolver.Resolve(
                    source,
                    MetadataTokens.GetToken(
                        MetadataTokens.StandaloneSignatureHandle(row)),
                    CliGenericContext.Empty);
            }
            catch (BadImageFormatException)
            {
                // Local signatures are also stored in this table; continue to the next row.
            }
            catch (CompilerException)
            {
                // Unsupported calling conventions are not call-site signatures for this contract.
            }
        }

        Assert.NotNull(signature);
    }

    [Fact]
    public void ResolveRejectsAStandaloneSignatureWithAnUnsupportedCallingConvention()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        var blob = new BlobBuilder();
        blob.WriteByte(0x01);
        blob.WriteByte(0x00);
        blob.WriteByte(0x01);
        var handle = metadata.AddStandaloneSignature(metadata.GetOrAddBlob(blob));
        var root = new MetadataRootBuilder(metadata);
        var image = new BlobBuilder();
        root.Serialize(image, 1, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var reader = provider.GetMetadataReader();
        var source = new MetadataAssemblySnapshot(
            new AssemblyIdentity("Synthetic"),
            reader,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());

        var exception = Assert.Throws<CompilerException>(() =>
            new MetadataCallSiteSignatureResolver().Resolve(
                source,
                MetadataTokens.GetToken(handle),
                CliGenericContext.Empty));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }
}
