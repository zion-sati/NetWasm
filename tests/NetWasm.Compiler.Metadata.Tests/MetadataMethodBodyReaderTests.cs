// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodBodyReaderTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PhysicalBodiesAndOrdinaryExternsUseTheDecoderWithoutCreatingAnAccessorResolver(
        bool physical, bool annotated, bool instance)
    {
        using var fixture = new Fixture(physical, annotated);
        var reader = fixture.CreateReader();

        var result = instance ? reader.ReadMethodBody(fixture.Method) : reader.ReadMethodBody(fixture.Method.Definition);

        // A definition decodes for its instance, as runtime-provided bodies are built.
        Assert.Same(fixture.Body, result);
        Assert.Equal(instance ? 1 : 2, fixture.AssemblyReads);
        Assert.Equal(1, fixture.DecoderCreations);
        Assert.Equal(1, fixture.InstanceDecodes);
        Assert.Equal(instance ? 0 : 1, fixture.MethodResolutions);
        Assert.Equal(1, fixture.IntrinsicBuilds);
        Assert.Equal(0, fixture.AccessorCreations);
        Assert.Equal(0, fixture.Bindings);
        Assert.Equal(0, fixture.BodyBuilds);
        Assert.Equal(0, fixture.DelegateCallBuilds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodylessDelegateAsynchronousCallsUseTheRuntimeProvidedBody(bool instance)
    {
        using var fixture = new Fixture(physical: false, annotated: false, delegateCall: true);

        var reader = fixture.CreateReader();
        var result = instance ? reader.ReadMethodBody(fixture.Method) : reader.ReadMethodBody(fixture.Method.Definition);

        Assert.Same(fixture.Body, result);
        Assert.Equal(1, fixture.DelegateCallBuilds);
        Assert.Equal(instance ? 0 : 1, fixture.MethodResolutions);
        Assert.Equal(0, fixture.DecoderCreations);
        Assert.Equal(0, fixture.AccessorCreations);
        Assert.Equal(0, fixture.BodyBuilds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodylessAccessorsBindTheirClosedInstanceAndReuseTheLazyResolver(bool instance)
    {
        using var fixture = new Fixture(physical: false, annotated: true);
        var reader = fixture.CreateReader();
        for (var read = 0; read < 2; read++)
        {
            var result = instance ? reader.ReadMethodBody(fixture.Method) : reader.ReadMethodBody(fixture.Method.Definition);
            Assert.Same(fixture.Body, result);
        }

        Assert.Equal(instance ? 0 : 2, fixture.AssemblyReads);
        Assert.Equal(instance ? 0 : 2, fixture.MethodResolutions);
        Assert.Equal(1, fixture.AccessorCreations);
        Assert.Equal(2, fixture.Bindings);
        Assert.Equal(2, fixture.BodyBuilds);
        Assert.Equal(0, fixture.DecoderCreations);
        Assert.Equal(0, fixture.InstanceDecodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsafeIntrinsicsUseTheCompilerProvidedBodyWithoutDecoding(bool instance)
    {
        using var fixture = new Fixture(physical: true, annotated: false) { IntrinsicBody = true };

        var reader = fixture.CreateReader();
        var result = instance ? reader.ReadMethodBody(fixture.Method) : reader.ReadMethodBody(fixture.Method.Definition);

        Assert.Same(fixture.Body, result);
        Assert.Equal(1, fixture.IntrinsicBuilds);
        Assert.Equal(instance ? 0 : 1, fixture.MethodResolutions);
        Assert.Equal(0, fixture.DecoderCreations);
        Assert.Equal(0, fixture.InstanceDecodes);
    }

    [Fact]
    public void BindingFailurePropagatesWithoutDecodingOrBuildingABody()
    {
        using var fixture = new Fixture(physical: false, annotated: true);
        fixture.BindingError = new InvalidOperationException("unsupported accessor shape");

        Assert.Same(fixture.BindingError, Assert.Throws<InvalidOperationException>(() =>
            fixture.CreateReader().ReadMethodBody(fixture.Method)));
        Assert.Equal(1, fixture.Bindings);
        Assert.Equal(0, fixture.BodyBuilds);
        Assert.Equal(0, fixture.DecoderCreations);
    }

    // A P/Invoke that takes runtime marshalling has no physical body; its stub builder makes one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodylessMarshallingStubsUseTheStubBody(bool instance)
    {
        using var fixture = new Fixture(physical: false, annotated: false, marshallingStub: true);

        var reader = fixture.CreateReader();
        var result = instance ? reader.ReadMethodBody(fixture.Method) : reader.ReadMethodBody(fixture.Method.Definition);

        Assert.Same(fixture.Body, result);
        Assert.Equal(1, fixture.MarshallingStubBuilds);
        Assert.Equal(0, fixture.UnboundNativeImportBuilds);
        Assert.Equal(0, fixture.DecoderCreations);
        Assert.Equal(0, fixture.DelegateCallBuilds);
    }

    // A body naming a member its declaring type lacks becomes the missing-member body; any other failure to decode,
    // an ambiguous reference among them, still fails the compile.
    [Fact]
    public void AMissingMemberDecodesToTheMissingMemberBodyAndOtherFailuresPropagate()
    {
        using var fixture = new Fixture(physical: true, annotated: false);
        var missing = new MissingMemberReference(MissingMemberKind.Field, "System.Int32 Owner.Gone", "formatted accessor", 4,
            "missing");
        fixture.DecodeError = new CompilerException(new(DiagnosticCode.MissingApi, "missing", "formatted accessor", 4)
        {
            MissingMember = missing,
        });

        Assert.Same(fixture.Body, fixture.CreateReader().ReadMethodBody(fixture.Method));
        Assert.Same(missing, fixture.BuiltMissingMember);

        var ambiguous = new CompilerException(new(DiagnosticCode.AssemblyResolution, "ambiguous"));
        fixture.DecodeError = ambiguous;
        Assert.Same(ambiguous, Assert.Throws<CompilerException>(() => fixture.CreateReader().ReadMethodBody(fixture.Method)));
        var unexplained = new CompilerException(new(DiagnosticCode.MissingApi, "missing without its member"));
        fixture.DecodeError = unexplained;
        Assert.Same(unexplained, Assert.Throws<CompilerException>(() => fixture.CreateReader().ReadMethodBody(fixture.Method)));
    }

    // A type its assembly lacks, met while decoding (a member reference's type) or named by a type token after it,
    // makes the body the missing-member body too, placed in the method decoding it.
    [Fact]
    public void AMissingTypeDecodesToTheMissingMemberBodyWhereverItSurfaces()
    {
        using var fixture = new Fixture(physical: true, annotated: false);
        var type = new MissingMemberReference(MissingMemberKind.Type, "Gone.Type", "", 0, "not found") { Assembly = "Gone" };
        fixture.DecodeError = new CompilerException(new(DiagnosticCode.AssemblyResolution, "not found", "formatted accessor", 6)
        {
            MissingMember = type,
        });

        Assert.Same(fixture.Body, fixture.CreateReader().ReadMethodBody(fixture.Method));
        Assert.Equal(type with { Method = "formatted accessor", IlOffset = 6 }, fixture.BuiltMissingMember);
        Assert.Equal("Could not load type 'Gone.Type' from assembly 'Gone'.", fixture.BuiltMissingMember!.ExceptionMessage);
        Assert.Equal("System.TypeLoadException", fixture.BuiltMissingMember.ExceptionType);

        fixture.DecodeError = null;
        fixture.MissingType = type with { Method = "formatted", IlOffset = 2 };
        Assert.Same(fixture.Body, fixture.CreateReader().ReadMethodBody(fixture.Method));
        Assert.Same(fixture.MissingType, fixture.BuiltMissingMember);
    }

    private sealed class Fixture : IDisposable, ICilDecoderFactory, ICilDecoder,
        IManagedAssemblyResolver, IMetadataMethodReferenceResolver, ISymbolFormatter,
        IUnsafeAccessorTargetResolverFactory, IUnsafeAccessorTargetResolver, IUnsafeAccessorBodyBuilder,
        IDelegateAsynchronousCallBodyBuilder, IUnsafeIntrinsicBodyBuilder, IUnboundNativeImportBodyBuilder,
        INativeMarshallingStubBodyBuilder, IMissingMemberBodyBuilder, IMissingTypeFinder
    {
        private readonly ManagedAssembly _assembly;
        private readonly UnsafeAccessorBinding _binding;
        internal MethodInstanceModel Method { get; }
        internal CilMethodBody Body { get; }
        internal int AssemblyReads, DecoderCreations, InstanceDecodes,
            MethodResolutions, AccessorCreations, Bindings, BodyBuilds, DelegateCallBuilds, IntrinsicBuilds, UnboundNativeImportBuilds,
            MarshallingStubBuilds;
        internal Exception? BindingError { get; set; }
        internal Exception? DecodeError { get; set; }
        internal MissingMemberReference? BuiltMissingMember { get; private set; }
        internal MissingMemberReference? MissingType { get; set; }
        // Whether the method is an Unsafe intrinsic, whose body the compiler builds.
        internal bool IntrinsicBody { get; init; }

        internal Fixture(bool physical, bool annotated, bool delegateCall = false, bool marshallingStub = false)
        {
            var imageBuilder = new PersistedAssemblyBuilder(new AssemblyName("Accessors"), typeof(object).Assembly);
            _ = imageBuilder.DefineDynamicModule("Accessors").DefineType("Owner").CreateType();
            using var image = new MemoryStream();
            imageBuilder.Save(image);
            _assembly = ManagedAssembly.Parse("memory/accessors.dll", image.ToArray(),
                AssemblyIdentityAliases.Empty, new ValueTypeDefinitionStackKindResolver(), new NativeImportDeclarationReader());
            var signature = new MethodSignatureModel(CliValueKind.Void, []);
            var definition = new MethodDefinitionModel(new(_assembly.Identity, 0x06000001),
                new(_assembly.Identity, 0x02000002), "Accessor", true, signature, physical ? 1 : 0)
            {
                UnsafeAccessor = annotated ? new UnsafeAccessorDeclaration(1, "Target", true, false) : null,
                IsDelegateAsynchronousCall = delegateCall,
                NativeMarshallingStub = marshallingStub
                    ? new(new(_assembly.Identity, unchecked((int)0x86000001)))
                    : null,
            };
            Method = new(definition, CliTypeIdentity.Named(_assembly.Identity, "", "Owner", false), [], signature);
            Body = new(definition, 1, [], []) { MethodInstance = Method };
            _binding = new UnsafeAccessorBinding.Method(Method);
        }

        internal IMethodBodyReader CreateReader() => Assert.IsAssignableFrom<IMethodBodyReader>(
            new MetadataMethodBodyReader(this, this, this, this, this, this, this, this, this, this, this, this));
        public void Dispose() => _assembly.Dispose();

        ICilDecoder ICilDecoderFactory.Create() { DecoderCreations++; return this; }
        public CilMethodBody Decode(ManagedAssembly source, MethodInstanceModel method)
        {
            Assert.Same(_assembly, source);
            Assert.Same(Method, method);
            InstanceDecodes++;
            if (DecodeError is not null) throw DecodeError;
            return Body;
        }
        public ManagedAssembly Resolve(AssemblyIdentity identity)
        {
            Assert.Equal(_assembly.Identity, identity);
            AssemblyReads++;
            return _assembly;
        }
        public MethodInstanceModel Resolve(MetadataAssemblySnapshot source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Assert.Same(_assembly.Metadata, source);
            Assert.Equal(Method.Definition.Key.MetadataToken, metadataToken);
            Assert.Equal("formatted accessor", methodDisplayName);
            Assert.Equal(0, ilOffset);
            Assert.Null(genericContext);
            MethodResolutions++;
            return Method;
        }
        public string Format(MethodDefinitionModel method)
        {
            Assert.Same(Method.Definition, method);
            return "formatted accessor";
        }
        public string Format(EntityKey key) => throw new NotSupportedException();
        IUnsafeAccessorTargetResolver IUnsafeAccessorTargetResolverFactory.Create() { AccessorCreations++; return this; }
        public UnsafeAccessorBinding Resolve(MethodInstanceModel method)
        {
            Assert.Same(Method, method);
            Bindings++;
            if (BindingError is not null) throw BindingError;
            return _binding;
        }
        public CilMethodBody Build(MethodInstanceModel method, UnsafeAccessorBinding binding)
        {
            Assert.Same(Method, method);
            Assert.Same(_binding, binding);
            BodyBuilds++;
            return Body;
        }
        public CilMethodBody Build(MethodInstanceModel method)
        {
            Assert.Same(Method, method);
            DelegateCallBuilds++;
            return Body;
        }
        CilMethodBody? IUnsafeIntrinsicBodyBuilder.Build(MethodInstanceModel method)
        {
            Assert.Same(Method, method);
            IntrinsicBuilds++;
            return IntrinsicBody ? Body : null;
        }
        CilMethodBody IUnboundNativeImportBodyBuilder.Build(MethodInstanceModel method)
        {
            Assert.Same(Method, method);
            UnboundNativeImportBuilds++;
            return Body;
        }
        CilMethodBody INativeMarshallingStubBodyBuilder.Build(MethodInstanceModel method)
        {
            Assert.Same(Method, method);
            MarshallingStubBuilds++;
            return Body;
        }
        CilMethodBody IMissingMemberBodyBuilder.Build(MethodInstanceModel method, MissingMemberReference missing)
        {
            Assert.Same(Method, method);
            BuiltMissingMember = missing;
            return Body;
        }
        MissingMemberReference? IMissingTypeFinder.Find(CilMethodBody body, string method) => MissingType;
    }
}
