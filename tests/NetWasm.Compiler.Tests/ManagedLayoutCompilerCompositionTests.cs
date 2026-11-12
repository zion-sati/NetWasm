using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedLayoutCompilerCompositionTests
{
    [Fact]
    public void FactoryUsesInjectedTypeAndStaticDataBuilders()
    {
        using var assets = TestAssets.Create();
        using var compilation = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var typeLayouts = new RecordingTypeLayoutCompilerFactory();
        var staticData = new RecordingStaticDataBuilderFactory();
        var compiler = CompilerTestSupport.CreateManagedLayoutCompiler(
            typeLayouts,
            staticData);
        var metadata = compilation.Snapshot;
        var types = MetadataCompilationTestActors.Types(metadata);
        var fields = MetadataCompilationTestActors.Fields(metadata);
        var methods = MetadataCompilationTestActors.Methods(metadata);
        var symbols = MetadataCompilationTestActors.Symbols(metadata);
        var classifier = MetadataCompilationTestActors.TypeClassifier(metadata);
        var typeFinder = MetadataCompilationTestActors.TypeFinder(metadata);
        var typeDefinitions = MetadataCompilationTestActors.TypeDefinitions(metadata);
        var identities = MetadataCompilationTestActors.TypeIdentities(metadata);
        var methodInstances = MetadataCompilationTestActors.MethodInstances(metadata);
        var baseTypes = MetadataCompilationTestActors.EntityBaseTypes(metadata);
        var baseTypeIdentities = MetadataCompilationTestActors.BaseTypeIdentities(metadata);
        var identityBaseTypes = MetadataCompilationTestActors.IdentityBaseTypes(metadata);
        var implementedInterfaces = MetadataCompilationTestActors.ImplementedInterfaces(metadata);
        var methodImplementations = MetadataCompilationTestActors.MethodImplementations(metadata);
        var bodies = MetadataCompilationTestActors.MethodBodies(metadata);
        var methodFinder = MetadataCompilationTestActors.MethodFinder(metadata);
        var entry = methodFinder.FindMethod(
            metadata.EntryAssemblyIdentity,
            "NetWasm.Fixtures.Application.EntryPoint",
            "Run");
        var program = CompilerTestSupport.CreateAnalyzer(
            metadata, types, fields, methods, typeFinder, typeDefinitions, identities,
            baseTypeIdentities, implementedInterfaces, bodies, methodInstances,
            methodImplementations, symbols).Analyze(entry, []);

        var layouts = compiler.Compile(
            metadata,
            types,
            fields,
            typeFinder,
            typeDefinitions,
            identities,
            baseTypes,
            identityBaseTypes,
            program,
            WasmTarget.Wasm64);

        Assert.True(typeLayouts.Called);
        Assert.True(staticData.Called);
        Assert.Equal(WasmTarget.Wasm64, layouts.Target.Target);
        Assert.True(layouts.StaticDataEnd > 0);

    }

    private sealed class RecordingTypeLayoutCompilerFactory :
        IManagedTypeLayoutCompilerFactory
    {
        public bool Called { get; private set; }

        public IManagedTypeLayoutCompiler Create(
            MetadataCompilationSnapshot metadata,
            ITypeRepository typeRepository,
            IFieldRepository fields,
            ITypeFinder types,
            ITypeDefinitionResolver typeDefinitions,
            ITypeIdentityResolver identities,
            IMetadataEntityBaseTypeResolver entityBaseTypes,
            IMetadataIdentityBaseTypeResolver identityBaseTypes,
            ReachableProgram program,
            WasmTargetLayout target)
        {
            Called = true;
            var state = new ManagedValueLayoutState();
            var build = new NoopBuildPhase();
            var resolve = new NoopResolvePhase();
            return new ManagedTypeLayoutCompiler(
                new ManagedTypeLayoutBuildState(state),
                target,
                build,
                resolve,
                resolve,
                build,
                resolve,
                new NoopDelegateOffsetPhase());
        }
    }

    private sealed class RecordingStaticDataBuilderFactory :
        IManagedStaticDataBuilderFactory
    {
        public bool Called { get; private set; }

        public IManagedStaticDataBuilder Create(
            MetadataCompilationSnapshot metadata,
            ITypeRepository typeRepository,
            IFieldRepository fields,
            ITypeFinder types,
            ITypeDefinitionResolver typeDefinitions,
            ITypeIdentityResolver identities,
            IMetadataIdentityBaseTypeResolver identityBaseTypes,
            ReachableProgram program,
            ManagedTypeLayouts layouts)
        {
            Called = true;
            var phase = new NoopStaticBuildPhase();
            return new ManagedStaticDataBuilder(
                layouts,
                new ManagedStaticDataBuildState(),
                phase,
                phase,
                phase,
                phase,
                phase,
                phase,
                phase,
                phase,
                phase,
                phase,
                phase,
                phase);
        }
    }

    private sealed class NoopBuildPhase :
        IReachableObjectLayoutBuilder,
        IImplicitObjectLayoutBuilder
    {
        public void Build() { }
    }

    private sealed class NoopResolvePhase :
        IStaticFieldValueLayoutResolver,
        IMethodValueLayoutResolver,
        IValueTypeLayoutResolver
    {
        public void Resolve() { }
    }

    private sealed class NoopDelegateOffsetPhase : IDelegateFieldOffsetResolver
    {
        public ManagedDelegateFieldOffsets Resolve() => new(-1, -1, -1, -1);
    }

    private sealed class NoopStaticBuildPhase :
        IStaticFieldStorageBuilder,
        ITypeDescriptorBuilder,
        IConstructedTypeDescriptorBuilder,
        IValueTypeDescriptorBuilder,
        IRuntimeGenericArgumentMetadataBuilder,
        IRuntimeTypeTableBuilder,
        IMemberDescriptorDataBuilder,
        IStringDataBuilder,
        IExceptionObjectBuilder,
        ITypeObjectDataBuilder,
        IEnumMetadataCollector,
        IEnumMetadataBuilder
    {
        public void Build() { }
        public void Collect() { }
    }
}
