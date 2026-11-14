using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryMethodSpecializerFactoryTests
{
    [Fact]
    public void FeatureRegistrationProvidesTheSpecializerFactory()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IMetadataCompilationMaterializationFactory, MetadataCompilationMaterializationFactory>()
            .AddAttributeAnalysis().BuildServiceProvider();
        Assert.IsType<AttributeQueryMethodSpecializerFactory>(
            services.GetRequiredService<IAttributeQueryMethodSpecializerFactory>());
    }

    [Fact]
    public void AnAssemblyWithoutAttributeContractsKeepsItsOriginalSpecializer()
    {
        using var fixture = new Fixture();
        Assert.Same(fixture, fixture.Create(includeContracts: false));
        Assert.Equal(0, fixture.ConstructorResolutions);
    }

    [Fact]
    public void BoundFeaturePreservesBodiesWithoutQueriesAfterSpecialization()
    {
        using var fixture = new Fixture();
        var factoryResult = fixture.Create();
        Assert.IsType<AttributeQueryMethodSpecializer>(factoryResult);
        Assert.Same(fixture.Specialized, factoryResult.Rewrite(fixture.Input));
        Assert.Equal(1, fixture.ConstructorResolutions);
        Assert.Equal(1, fixture.Specializations);
    }

    [Theory]
    [InlineData("System.Attribute", true)]
    [InlineData("System.Reflection.CustomAttributeExtensions", true)]
    [InlineData("System.Type", false)]
    [InlineData("System.Reflection.MemberInfo", false)]
    [InlineData("System.Reflection.ICustomAttributeProvider", false)]
    public void EveryBoundQueryOwnerKeepsUnfilteredEnumerationManaged(string owner, bool isStatic)
    {
        using var fixture = new Fixture();
        var definition = new MethodDefinitionModel(new(fixture.Assembly, 0x060000ff),
            fixture.FindType(owner).Key, "GetCustomAttributes", isStatic,
            MethodSignatureModel.Create(CliValueKind.ManagedReference), 0);
        fixture.Query = new(definition, CliTypeIdentity.FromDefinition(fixture.FindType(owner)), [], definition.Signature);
        // Unfiltered overloads keep their managed bodies and catchable platform fallbacks.
        Assert.Same(fixture.Specialized, fixture.Create().Rewrite(fixture.Input));
        Assert.Equal(1, fixture.Specializations);
    }

    private sealed class Fixture : IDisposable, IMethodSpecializer, ITypeRepository, IFieldRepository,
        IMethodRepository, ITypeFinder, ITypeDefinitionResolver, ITypeIdentityResolver,
        IMethodInstanceResolver, ITypeRelationshipClassifier, IBaseTypeResolver, ICalledMethodResolver,
        ITypeOperandResolver, ISymbolFormatter, IManagedAssemblyImageReader
    {
        private readonly ManagedAssembly _assembly;
        private readonly MetadataCompilationSnapshot _metadata;
        private readonly byte[] _image;
        internal AssemblyIdentity Assembly => _assembly.Identity;
        internal CilMethodBody Input { get; }
        internal CilMethodBody Specialized { get; }
        internal MethodInstanceModel? Query { get; set; }
        internal int ConstructorResolutions { get; private set; }
        internal int Specializations { get; private set; }

        internal Fixture()
        {
            // A metadata-only in-memory assembly exercises factory composition;
            // behavioral inputs are supplied through the interfaces below.
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("AttributeComposition"), default, default, default);
            metadata.AddAssembly(metadata.GetOrAddString("AttributeComposition"), new(1, 0), default, default, 0, 0);
            var methodRow = 1;
            AddType("System", "Object");
            AddType("System", "Attribute");
            AddType("System", "Type");
            AddType("System.Reflection", "MemberInfo");
            AddType("System.Reflection", "ICustomAttributeProvider");
            AddType("System.Reflection", "CustomAttributeExtensions");
            AddType("System", "Nullable", ("Other", true, 0), ("GetUnderlyingType", false, 0), ("GetUnderlyingType", true, 1));
            AddType("System", "Nullable`1");
            AddType("System.Reflection", "AmbiguousMatchException", ("Other", false, 0),
                (".ctor", true, 0), (".ctor", false, 1), (".ctor", false, 0));
            AddType("System", "SerializableAttribute");
            AddType("System.Runtime.InteropServices", "ComImportAttribute");
            AddType("System.Runtime.InteropServices", "StructLayoutAttribute");
            AddType("System", "AttributeUsageAttribute");
            AddType("System", "Action`1", ("Other", false, 0), (".ctor", false, 0));
            AddType("System.Runtime.CompilerServices", "RuntimeCustomAttributes", ("Other", true, 0), ("ApplySetter", true, 0));
            var image = new BlobBuilder();
            new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new()).Serialize(image);
            _image = image.ToArray();
            _assembly = new ManagedAssemblyLoader(this, new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(), new NativeCallbackDeclarationReader()).Load("memory.dll");
            var unrelated = new TypeDefinitionModel(new(new("Application"), 0x02000001),
                "System", "Attribute", false, [], []);
            _metadata = new([_assembly], Assembly, [.. _assembly.Types.Values, unrelated],
                [.. _assembly.Methods.Values], []);
            Input = new(new(new(Assembly, 0x060000fe), FindType("System.Object").Key, "Run", true,
                MethodSignatureModel.Create(CliValueKind.Void), 0), 0, [], []);
            Specialized = Input with { Instructions = [new(0, 1, CilOperation.Nop, new CilOperand.None())], MaxStack = 1 };

            void AddType(string ns, string name, params (string Name, bool Static, int Parameters)[] methods)
            {
                metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString(ns),
                    metadata.GetOrAddString(name), default, MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(methodRow));
                foreach (var method in methods)
                {
                    var signature = new BlobBuilder();
                    new BlobEncoder(signature).MethodSignature(isInstanceMethod: !method.Static)
                        .Parameters(method.Parameters, result => result.Void(), parameters =>
                        {
                            if (method.Parameters != 0) parameters.AddParameter().Type().Int32();
                        });
                    metadata.AddMethodDefinition(MethodAttributes.Public | (method.Static ? MethodAttributes.Static : 0),
                        MethodImplAttributes.IL, metadata.GetOrAddString(method.Name), metadata.GetOrAddBlob(signature),
                        -1, MetadataTokens.ParameterHandle(1));
                    methodRow++;
                }
            }
        }

        internal IMethodSpecializer Create(bool includeContracts = true) =>
            ((IAttributeQueryMethodSpecializerFactory)new AttributeQueryMethodSpecializerFactory(new MetadataCompilationMaterializationFactory())).Create(this,
                includeContracts ? _metadata : _metadata with { Types = [FindType("System.Object")] },
                this, this, this, this, this, this, this, this, this, this, this, this);

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            Assert.Same(Input, body);
            Specializations++;
            return Specialized;
        }

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            var method = GetMethod(new(source, metadataToken));
            Assert.Equal(FindType("System.Reflection.AmbiguousMatchException").Key, method.DeclaringType);
            Assert.Equal(".ctor", methodDisplayName);
            Assert.False(method.IsStatic);
            Assert.Empty(method.Signature.ParameterTypes);
            Assert.Equal(0, ilOffset);
            Assert.Null(genericContext);
            ConstructorResolutions++;
            return new(method, GetTypeIdentity(method.DeclaringType), [], method.Signature);
        }

        public TypeDefinitionModel FindType(string fullName) => _assembly.Types.Values.Single(type => type.FullName == fullName);
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _assembly.Types[key.MetadataToken];
        public MethodDefinitionModel GetMethod(EntityKey key) => _assembly.Methods[key.MetadataToken];
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => CliTypeIdentity.FromDefinition(GetTypeDefinition(type));
        public MethodInstanceModel? Resolve(CilInstruction instruction) => Query;
        public string Format(MethodDefinitionModel method) => method.Name;
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => throw new NotSupportedException();
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target) => throw new NotSupportedException();
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => throw new NotSupportedException();
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) => throw new NotSupportedException();
        public string Format(EntityKey key) => throw new NotSupportedException();
        public byte[] Read(string path)
        {
            Assert.Equal("memory.dll", path);
            return _image;
        }
        public void Dispose() => _assembly.Dispose();
    }
}
