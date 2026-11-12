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

    // As in CoreCLR, only the unmanaged header (0x09) reads conventions from the return type, and
    // only from optional modifiers: a C header's (0x01) modifier and a required one name nothing.
    [Theory]
    [InlineData(0x01, (byte)0)]
    [InlineData(0x01, (byte)0x20)]
    [InlineData(0x09, (byte)0x1f)]
    [InlineData(0x09, (byte)0x20)]
    public void OnlyAnUnmanagedHeadersOptionalModifiersNameItsConvention(byte header, byte modifier)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        var coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"), new Version(11, 0, 0, 0), default, default, default, default);
        var fastcall = metadata.AddTypeReference(
            coreLib, metadata.GetOrAddString("System.Runtime.CompilerServices"), metadata.GetOrAddString("CallConvFastcall"));
        var blob = new BlobBuilder();
        blob.WriteByte(header);
        blob.WriteByte(0x00);
        if (modifier != 0)
        {
            blob.WriteByte(modifier);
            blob.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(fastcall));
        }
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

        CilOperand.CallSite Resolve() => new MetadataCallSiteSignatureResolver().Resolve(
            source,
            MetadataTokens.GetToken(handle),
            CliGenericContext.Empty,
            "M",
            0);

        if (header == 0x09 && modifier == 0x20)
        {
            Assert.Contains("CallConvFastcall' is not supported",
                Assert.Throws<CompilerException>(Resolve).Diagnostic.Message, StringComparison.Ordinal);
            return;
        }
        var callSite = Resolve();
        Assert.True(callSite.Unmanaged);
        Assert.Equal(CliTypeShape.Primitive, callSite.Signature.ReturnSignatureType.Shape);
        Assert.Equal(CliValueKind.Void, callSite.Signature.ReturnType);
    }

    // Issue 226: an unmanaged pointer is called with the C ABI under each convention desktop .NET 11
    // calls it with (the default, Cdecl, Stdcall, Thiscall, with SuppressGCTransition or
    // MemberFunction), and drops the modifiers the C ABI does not read; Fastcall, Swift and two base
    // conventions fail as on desktop, naming the method and IL offset. A managed pointer keeps its
    // signature (ref readonly returns included).
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
                public static ref readonly int ManagedReadonly(delegate*<in int, ref readonly int> target, in int value) => ref target(in value);
                public static int Unmanaged(delegate* unmanaged<long, int> target) => target(2);
                public static int Cdecl(delegate* unmanaged[Cdecl]<float, int> target) => target(3);
                public static int Stdcall(delegate* unmanaged[Stdcall]<double, int> target) => target(4);
                public static int Thiscall(delegate* unmanaged[Thiscall]<nint, int> target) => target(5);
                public static int CdeclSuppressed(delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int> target) => target(6);
                public static ref readonly int StdcallMember(delegate* unmanaged[Stdcall, MemberFunction]<ref readonly int> target) => ref target();
                public static int Fastcall(delegate* unmanaged[Fastcall]<int, int> target) => target(7);
                public static int Swift(delegate* unmanaged[Swift]<int, int> target) => target(8);
                public static int Both(delegate* unmanaged[Cdecl, Stdcall]<int, int> target) => target(9);
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

        Assert.Equal(2, resolved.Count(site => !site.Unmanaged));
        Assert.Single(resolved, site => !site.Unmanaged && site.Signature.ReturnType == CliValueKind.ManagedAddress);
        Assert.Equal(6, resolved.Count(site => site.Unmanaged));
        Assert.All(resolved.Where(site => site.Unmanaged), site =>
            Assert.NotEqual(CliTypeShape.Modified, site.Signature.ReturnSignatureType.Shape));
        Assert.Equal(3, failures.Count);
        Assert.All(failures, diagnostic =>
        {
            Assert.Equal(DiagnosticCode.NativeInterop, diagnostic.Code);
            Assert.Equal(("M", 7), (diagnostic.Method, diagnostic.IlOffset));
        });
        Assert.Contains(failures, diagnostic => diagnostic.Message.Contains("calling convention 'FastCall' is not supported", StringComparison.Ordinal));
        Assert.Contains(failures, diagnostic => diagnostic.Message.Contains("CallConvSwift' is not supported", StringComparison.Ordinal));
        Assert.Contains(failures, diagnostic => diagnostic.Message.Contains("are both named", StringComparison.Ordinal));
    }
}
