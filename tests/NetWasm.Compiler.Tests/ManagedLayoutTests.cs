using System.Buffers.Binary;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedLayoutTests
{
    [Fact]
    public void Memory64LayoutsWidenReferencesButKeepSemanticLengthsAndIdsAtI32()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "Memory64LayoutFixture",
            """
            namespace Memory64LayoutFixture;

            public sealed class Node
            {
                public string Text = "x";
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new Node[1];
                    values[0] = new Node();
                    return input;
                }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
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
            "Memory64LayoutFixture.EntryPoint",
            "Run");
        var program = CompilerTestSupport.CreateAnalyzer(
            metadata, types, fields, methods, typeFinder, typeDefinitions,
            identities, baseTypeIdentities, implementedInterfaces, bodies,
            methodInstances, methodImplementations, symbols).Analyze(entry, []);
        var layouts = CompilerTestSupport.CreateLayouts(
            metadata, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, program, WasmTarget.Wasm64);
        var typeLayouts = CompilerTestSupport.CreateTypeLayouts(typeDefinitions, layouts);
        var staticData = CompilerTestSupport.CreateStaticData(layouts);

        var node = typeFinder.FindType("Memory64LayoutFixture.Node");
        var nodeLayout = typeLayouts.GetObjectLayout(node.Key);
        var nodeIdentity = identities.GetTypeIdentity(node.Key);
        Assert.Equal(
            typeLayouts.GetObjectLayout(nodeIdentity),
            typeLayouts.GetObjectLayout(nodeIdentity));
        var arrayLayout = typeLayouts.GetObjectLayout(
            typeFinder.FindType("System.Array").Key);
        var literal = staticData.GetStringLayout("x");
        var literalSegment = layouts.DataSegments.Single(segment =>
            segment.Address == literal.Address);

        Assert.Equal(WasmTarget.Wasm64, layouts.Target.Target);
        Assert.Equal(8, layouts.Target.ObjectHeaderSize);
        Assert.Equal(layouts.Target.ObjectHeaderSize, layouts.StringLengthOffset);
        Assert.Equal(8, ((IRuntimeObjectLayout)layouts).StringLengthOffset);
        Assert.Equal(12, ((IRuntimeObjectLayout)layouts).StringDataOffset);
        Assert.Equal(8, ((IRuntimeObjectLayout)layouts).ArrayLengthOffset);
        Assert.Equal(16, ((IRuntimeObjectLayout)layouts).ArrayDataPointerOffset);
        Assert.True(nodeLayout.ReferenceOffsets.AsSpan().SequenceEqual([8]));
        Assert.Equal(16, nodeLayout.Size);
        Assert.True(arrayLayout.ReferenceOffsets.AsSpan().SequenceEqual([16]));
        Assert.Equal(32, arrayLayout.Size);
        Assert.Equal(12, literal.DataOffset);
        Assert.Equal(16, literalSegment.Data.Length);
        Assert.Equal(layouts.StringTypeId,
            BinaryPrimitives.ReadInt32LittleEndian(literalSegment.Data.AsSpan()));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(
            literalSegment.Data.AsSpan()[8..]));
        Assert.Equal((ushort)'x', BinaryPrimitives.ReadUInt16LittleEndian(
            literalSegment.Data.AsSpan()[12..]));
        var nodeDescriptor = layouts.TypeDescriptors.Single(descriptor =>
            descriptor.Type == node.Key);
        // Its second 8-byte slot holds a reference.
        Assert.Equal([8], nodeDescriptor.ReferenceOffsets);

    }

    [Fact]
    public void AnalyzerAndLayoutProviderValidateArgumentsAndMissingLayouts()
    {
        using var assets = TestAssets.Create();
        using var compilation = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
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
        Assert.Throws<ArgumentNullException>(() => CompilerTestSupport.CreateAnalyzer(
            null!, types, fields, methods, typeFinder, typeDefinitions, identities,
            baseTypeIdentities, implementedInterfaces, bodies, methodInstances,
            methodImplementations, symbols));
        var analyzer = CompilerTestSupport.CreateAnalyzer(
            metadata, types, fields, methods, typeFinder, typeDefinitions, identities,
            baseTypeIdentities, implementedInterfaces, bodies, methodInstances,
            methodImplementations, symbols);
        Assert.Null(analyzer.Analyze(null, []).EntryPoint);
        var entry = methodFinder.FindMethod(
            metadata.EntryAssemblyIdentity,
            "NetWasm.Fixtures.Application.EntryPoint",
            "Run");
        Assert.Throws<ArgumentNullException>(() => analyzer.Analyze(entry, null!));

        var program = analyzer.Analyze(entry, []);
        var layouts = CompilerTestSupport.CreateLayouts(
            metadata, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, program);
        var typeLayouts = CompilerTestSupport.CreateTypeLayouts(typeDefinitions, layouts);
        var instanceFields = CompilerTestSupport.CreateInstanceFields(layouts);
        var staticFields = CompilerTestSupport.CreateStaticFields(layouts);
        var staticData = CompilerTestSupport.CreateStaticData(layouts);
        var exceptionObjects = CompilerTestSupport.CreateExceptionObjects(layouts);
        var missing = new EntityKey(new AssemblyIdentity("Missing"), 1);
        Assert.Throws<CompilerException>(() => typeLayouts.GetObjectLayout(missing));
        Assert.Throws<CompilerException>(() => instanceFields.GetFieldLayout(missing));
        Assert.Throws<CompilerException>(() => staticFields.GetStaticFieldLayout(missing));
        Assert.Throws<CompilerException>(() => staticData.GetStringLayout("missing"));
        Assert.Throws<CompilerException>(() =>
            exceptionObjects.GetExceptionObject((ManagedExceptionKind)int.MaxValue));
        Assert.Throws<ArgumentNullException>(() => CompilerTestSupport.CreateLayouts(
            null!, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, program));
        Assert.Throws<ArgumentNullException>(() => CompilerTestSupport.CreateLayouts(
            metadata, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, null!));

    }

    [Fact]
    public void LayoutProviderDerivesInheritedReferenceBitmapAndStaticRoots()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "LayoutFixture",
            """
            namespace LayoutFixture;

            public class BaseNode
            {
                public int Scalar;
                public string BaseReference;
            }

            public sealed class Node : BaseNode
            {
                public int Value;
                public string Child;
                public static Node Root;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Node node = new Node();
                    node.BaseReference = "base";
                    node.Child = "child";
                    node.Value = input;
                    Node.Root = node;
                    return node.Value;
                }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
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
            metadata.EntryAssemblyIdentity, "LayoutFixture.EntryPoint", "Run");
        var program = CompilerTestSupport.CreateAnalyzer(
            metadata, types, fields, methods, typeFinder, typeDefinitions, identities,
            baseTypeIdentities, implementedInterfaces, bodies, methodInstances,
            methodImplementations, symbols).Analyze(entry, []);
        var layouts = CompilerTestSupport.CreateLayouts(
            metadata, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, program);
        var typeLayouts = CompilerTestSupport.CreateTypeLayouts(typeDefinitions, layouts);
        var instanceFields = CompilerTestSupport.CreateInstanceFields(layouts);
        var staticFields = CompilerTestSupport.CreateStaticFields(layouts);
        var node = metadata.Types.Single(type => type.FullName == "LayoutFixture.Node");
        var objectLayout = typeLayouts.GetObjectLayout(node.Key);
        var nodeFields = metadata.Assemblies
            .SelectMany(candidate => candidate.Fields.Values)
            .Where(field => field.DeclaringType == node.Key)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
        var baseNode = metadata.Types.Single(type => type.FullName == "LayoutFixture.BaseNode");
        var baseReference = baseNode.Fields
            .Select(fields.GetField)
            .Single(field => field.Name == "BaseReference");

        Assert.True(objectLayout.ReferenceOffsets.AsSpan().SequenceEqual([8, 16]));
        Assert.Equal(20, objectLayout.Size);
        Assert.Equal(8, instanceFields.GetFieldLayout(baseReference.Key).Offset);
        Assert.Equal(16, instanceFields.GetFieldLayout(nodeFields["Child"].Key).Offset);
        var descriptor = layouts.TypeDescriptors.Single(candidate => candidate.Type == node.Key);
        Assert.Equal(objectLayout.TypeId, descriptor.TypeId);
        Assert.Equal(20, descriptor.ObjectSize);
        // References in the third and fifth 4-byte slots.
        Assert.Equal([8, 16], descriptor.ReferenceOffsets);
        // The image also freezes the OutOfMemoryException thrown when not even
        // a new one fits, and its reference fields are roots as Root is.
        var outOfMemory = CompilerTestSupport.CreateExceptionObjects(layouts)
            .GetExceptionObject(ManagedExceptionKind.OutOfMemory);
        var outOfMemoryLayout = typeLayouts.GetObjectLayout(
            typeFinder.FindType("System.OutOfMemoryException").Key);
        Assert.Equal(
            [
                staticFields.GetStaticFieldLayout(nodeFields["Root"].Key).Address,
                .. outOfMemoryLayout.ReferenceOffsets.Select(offset => outOfMemory + offset),
            ],
            layouts.StaticRootAddresses.ToArray());

    }

    [Fact]
    public void LayoutProviderBuildsCanonicalNestedValueLayoutsAndExactRoots()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueLayoutFixture",
            """
            namespace ValueLayoutFixture;

            public struct EmptyValue { }

            public struct ScalarPair
            {
                public int Left;
                public int Right;
            }

            public sealed class Node { }

            public struct ReferencePair
            {
                public Node First;
                public Node Second;
                public int Tag;
            }

            public struct NestedValue
            {
                public int Prefix;
                public ReferencePair Pair;
                public int Suffix;
            }

            public struct GenericPair<T>
            {
                public T First;
                public T Second;
            }

            public sealed class Holder
            {
                public NestedValue Value;
            }

            public static class EntryPoint
            {
                public static NestedValue Root;
                public static int Run(int input) => input;
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
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
            metadata.EntryAssemblyIdentity, "ValueLayoutFixture.EntryPoint", "Run");
        var baseline = CompilerTestSupport.CreateAnalyzer(
            metadata, types, fields, methods, typeFinder, typeDefinitions, identities,
            baseTypeIdentities, implementedInterfaces, bodies, methodInstances,
            methodImplementations, symbols).Analyze(entry, []);
        var holder = typeFinder.FindType("ValueLayoutFixture.Holder");
        var holderValue = holder.Fields.Select(fields.GetField)
            .Single(field => field.Name == "Value");
        var entryType = typeFinder.FindType("ValueLayoutFixture.EntryPoint");
        var staticRoot = entryType.Fields.Select(fields.GetField)
            .Single(field => field.Name == "Root");
        var program = baseline with
        {
            Types = baseline.Types.Add(holder.Key),
            Fields = baseline.Fields.Add(holderValue.Key).Add(staticRoot.Key),
        };
        var layouts = CompilerTestSupport.CreateLayouts(
            metadata, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, program);
        var valueLayouts = CompilerTestSupport.CreateValueLayouts(typeDefinitions, fields, layouts);
        var typeLayouts = CompilerTestSupport.CreateTypeLayouts(typeDefinitions, layouts);
        var instanceFields = CompilerTestSupport.CreateInstanceFields(layouts);
        var staticFields = CompilerTestSupport.CreateStaticFields(layouts);

        var empty = valueLayouts.Resolve(Named("EmptyValue"));
        var scalar = valueLayouts.Resolve(Named("ScalarPair"));
        var pair = valueLayouts.Resolve(Named("ReferencePair"));
        var nested = valueLayouts.Resolve(Named("NestedValue"));
        var genericDefinition = Named("GenericPair`1");
        var referenceGeneric = valueLayouts.Resolve(
            CliTypeIdentity.GenericInstantiation(
                genericDefinition,
                [CliTypeIdentity.Named(
                        metadata.EntryAssemblyIdentity,
                        "ValueLayoutFixture",
                        "Node",
                        isValueType: false)]));
        var valueGeneric = valueLayouts.Resolve(
            CliTypeIdentity.GenericInstantiation(genericDefinition, [scalar.Type]));

        Assert.Equal((1, 1), (empty.Size, empty.Alignment));
        Assert.True(empty.ReferenceOffsets.IsEmpty);
        Assert.Equal((8, 4), (scalar.Size, scalar.Alignment));
        Assert.Equal((12, 4), (pair.Size, pair.Alignment));
        Assert.True(pair.ReferenceOffsets.AsSpan().SequenceEqual([0, 4]));
        Assert.Equal((20, 4), (nested.Size, nested.Alignment));
        Assert.True(nested.ReferenceOffsets.AsSpan().SequenceEqual([4, 8]));
        Assert.Equal(8, referenceGeneric.Size);
        Assert.True(referenceGeneric.ReferenceOffsets.AsSpan().SequenceEqual([0, 4]));
        Assert.Equal(16, valueGeneric.Size);
        Assert.True(valueGeneric.ReferenceOffsets.IsEmpty);

        var holderField = instanceFields.GetFieldLayout(holderValue.Key);
        Assert.Equal(4, holderField.Offset);
        Assert.Equal(20, holderField.Size);
        Assert.True(typeLayouts.GetObjectLayout(holder.Key).ReferenceOffsets
            .AsSpan().SequenceEqual([8, 12]));
        var staticAddress = staticFields.GetStaticFieldLayout(staticRoot.Key).Address;
        Assert.True(layouts.StaticRootAddresses.AsSpan().SequenceEqual(
            [staticAddress + 4, staticAddress + 8]));
        CliTypeIdentity Named(string name) => CliTypeIdentity.Named(
            metadata.EntryAssemblyIdentity,
            "ValueLayoutFixture",
            name,
            isValueType: true);
    }

    [Fact]
    public void InlineArrayLayoutRepeatsTheElementStorage()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InlineArrayLayoutFixture",
            """
            namespace InlineArrayLayoutFixture;

            [System.Runtime.CompilerServices.InlineArray(3)]
            public struct Buffer
            {
                private int _element0;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Buffer buffer = default;
                    buffer[0] = input;
                    return buffer[0];
                }
            }
            """);
        using var compilation = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
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
            "InlineArrayLayoutFixture.EntryPoint",
            "Run");
        var program = CompilerTestSupport.CreateAnalyzer(
            metadata, types, fields, methods, typeFinder, typeDefinitions, identities,
            baseTypeIdentities, implementedInterfaces, bodies, methodInstances,
            methodImplementations, symbols).Analyze(entry, []);
        var layouts = CompilerTestSupport.CreateLayouts(
            metadata, types, fields, typeFinder, typeDefinitions, identities,
            baseTypes, identityBaseTypes, program);
        var valueLayouts = CompilerTestSupport.CreateValueLayouts(typeDefinitions, fields, layouts);
        var buffer = typeFinder.FindType("InlineArrayLayoutFixture.Buffer");
        var layout = valueLayouts.Resolve(CliTypeIdentity.Named(
            metadata.EntryAssemblyIdentity,
            "InlineArrayLayoutFixture",
            "Buffer",
            isValueType: true));

        Assert.Equal(3, buffer.InlineArrayLength);
        Assert.Equal((12, 4), (layout.Size, layout.Alignment));
        Assert.True(layout.ReferenceOffsets.IsEmpty);

    }
}
