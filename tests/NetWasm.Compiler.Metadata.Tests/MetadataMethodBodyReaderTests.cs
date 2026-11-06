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

        // A definition with a body decodes for its instance, as runtime-provided bodies are built.
        var resolved = !instance && physical;
        Assert.Same(fixture.Body, result);
        Assert.Equal(resolved ? 2 : 1, fixture.AssemblyReads);
        Assert.Equal(1, fixture.DecoderCreations);
        Assert.Equal(instance || resolved ? 0 : 1, fixture.DefinitionDecodes);
        Assert.Equal(instance || resolved ? 1 : 0, fixture.InstanceDecodes);
        Assert.Equal(resolved ? 1 : 0, fixture.MethodResolutions);
        Assert.Equal(instance || resolved ? 1 : 0, fixture.IntrinsicBuilds);
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
        Assert.Equal(0, fixture.DefinitionDecodes);
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
        Assert.Equal(0, fixture.DefinitionDecodes);
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

    private sealed class Fixture : IDisposable, ICilDecoderFactory, ICilDecoder,
        IManagedAssemblyResolver, IMetadataMethodReferenceResolver, ISymbolFormatter,
        IUnsafeAccessorTargetResolverFactory, IUnsafeAccessorTargetResolver, IUnsafeAccessorBodyBuilder,
        IDelegateAsynchronousCallBodyBuilder, IUnsafeIntrinsicBodyBuilder
    {
        private readonly ManagedAssembly _assembly;
        private readonly UnsafeAccessorBinding _binding;
        internal MethodInstanceModel Method { get; }
        internal CilMethodBody Body { get; }
        internal int AssemblyReads, DecoderCreations, DefinitionDecodes, InstanceDecodes,
            MethodResolutions, AccessorCreations, Bindings, BodyBuilds, DelegateCallBuilds, IntrinsicBuilds;
        internal Exception? BindingError { get; set; }
        // Whether the method is an Unsafe intrinsic, whose body the compiler builds.
        internal bool IntrinsicBody { get; init; }

        internal Fixture(bool physical, bool annotated, bool delegateCall = false)
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
                UnsafeAccessor = annotated ? new UnsafeAccessorDeclaration(1, "Target", true, false, false) : null,
                IsDelegateAsynchronousCall = delegateCall,
            };
            Method = new(definition, CliTypeIdentity.Named(_assembly.Identity, "", "Owner", false), [], signature);
            Body = new(definition, 1, [], []) { MethodInstance = Method };
            _binding = new UnsafeAccessorBinding.Method(Method);
        }

        internal IMethodBodyReader CreateReader() => Assert.IsAssignableFrom<IMethodBodyReader>(
            new MetadataMethodBodyReader(this, this, this, this, this, this, this, this));
        public void Dispose() => _assembly.Dispose();

        ICilDecoder ICilDecoderFactory.Create() { DecoderCreations++; return this; }
        public CilMethodBody Decode(ManagedAssembly source, MethodDefinitionModel method)
        {
            Assert.Same(_assembly, source);
            Assert.Same(Method.Definition, method);
            DefinitionDecodes++;
            return Body;
        }
        public CilMethodBody Decode(ManagedAssembly source, MethodInstanceModel method)
        {
            Assert.Same(_assembly, source);
            Assert.Same(Method, method);
            InstanceDecodes++;
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
    }
}
