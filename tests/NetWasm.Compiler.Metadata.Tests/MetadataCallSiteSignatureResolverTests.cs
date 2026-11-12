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
                resolver.Resolve(source, 0, CliGenericContext.Empty, "M", 3));

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
                    CliGenericContext.Empty,
                    "M",
                    0).Signature;
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

        // A C (0x01) call site through an unmanaged pointer is supported (issue 226).
        var callSite = new MetadataCallSiteSignatureResolver().Resolve(
            source,
            MetadataTokens.GetToken(handle),
            CliGenericContext.Empty,
            "M",
            0);

        Assert.True(callSite.Unmanaged);
        Assert.Equal(CliValueKind.Void, callSite.Signature.ReturnType);
    }

    // Issue 226: an unmanaged pointer's call site keeps its convention; only the default C ABI and
    // Cdecl are supported, and an unsupported one names the method and IL offset.
    [Fact]
    public void ResolveRecordsUnmanagedConventionsAndRejectsOthersWithTheirLocation()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "UnmanagedCallSites",
            """
            public static unsafe class EntryPoint
            {
                public static int Managed(delegate*<int, int> target) => target(1);
                public static int Unmanaged(delegate* unmanaged<int, int> target) => target(2);
                public static int Cdecl(delegate* unmanaged[Cdecl]<long, int> target) => target(3);
                public static int CdeclSuppressed(delegate* unmanaged[Cdecl, SuppressGCTransition]<float, int> target) => target(4);
                public static int Stdcall(delegate* unmanaged[Stdcall]<double, int> target) => target(5);
            }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var resolver = new MetadataCallSiteSignatureResolver();
        var resolved = new List<CilOperand.CallSite>();
        var failures = new List<CompilerDiagnostic>();
        for (var row = 1; row <= assembly.Reader.GetTableRowCount(TableIndex.StandAloneSig); row++)
        {
            var handle = MetadataTokens.StandaloneSignatureHandle(row);
            // Local signatures share the table; only method signatures are call sites.
            if (assembly.Reader.GetBlobReader(assembly.Reader.GetStandaloneSignature(handle).Signature)
                .ReadSignatureHeader().Kind != SignatureKind.Method)
            {
                continue;
            }
            try
            {
                resolved.Add(resolver.Resolve(source, MetadataTokens.GetToken(handle), CliGenericContext.Empty, "M", 7));
            }
            catch (CompilerException exception)
            {
                failures.Add(exception.Diagnostic);
            }
        }

        Assert.Equal(3, resolved.Count);
        Assert.Contains(resolved, site => !site.Unmanaged && site.Signature.ParameterTypes[0] == CliValueKind.I4);
        Assert.Contains(resolved, site => site.Unmanaged && site.Signature.ParameterTypes[0] == CliValueKind.I4);
        // Cdecl as the convention modifier is stripped from the result.
        var cdecl = Assert.Single(resolved, site => site.Signature.ParameterTypes[0] == CliValueKind.I8);
        Assert.True(cdecl.Unmanaged);
        Assert.Equal(CliTypeShape.Primitive, cdecl.Signature.ReturnSignatureType.Shape);
        Assert.Equal(2, failures.Count);
        Assert.All(failures, diagnostic =>
        {
            Assert.Equal(DiagnosticCode.NativeInterop, diagnostic.Code);
            Assert.Equal(("M", 7), (diagnostic.Method, diagnostic.IlOffset));
        });
        Assert.Contains(failures, diagnostic => diagnostic.Message.Contains("CallConvSuppressGCTransition", StringComparison.Ordinal));
        Assert.Contains(failures, diagnostic => diagnostic.Message.Contains("'StdCall'", StringComparison.Ordinal));
    }
}
