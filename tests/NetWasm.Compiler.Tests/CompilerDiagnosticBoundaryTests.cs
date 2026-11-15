// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticBoundaryTests
{
    [Fact]
    public void ResolvesArtifactsOnlyWhenDiagnosticsWereRequested()
    {
        var resolver = new CompilerDiagnosticArtifactPathResolver();
        var options = new CompilerOptions("entry.dll", [], "Entry", "Run", []) with
        {
            DiagnosticTracePath = "trace",
        };

        Assert.Equal(
            Path.Combine("trace.passes", "nested/value.json"),
            resolver.ResolvePath(options, "nested/value.json"));
        Assert.Null(resolver.ResolvePath(options with { DiagnosticTracePath = null }, "x"));
        Assert.Throws<ArgumentNullException>(() => resolver.ResolvePath(null!, "x"));
        Assert.Throws<ArgumentException>(() => resolver.ResolvePath(options, ""));
    }

    [Fact]
    public void FormatsCilOperandsThroughItsSingleCapability()
    {
        var formatter = new CompilerCilOperandFormatter();

        Assert.NotEmpty(formatter.FormatOperand(new NetWasm.Compiler.Core.CilOperand.None()));
        var method = ValidationTestData.Method();
        var type = new EntityKey(ValidationTestData.Assembly, 0x02000002);
        var field = new FieldDefinitionModel(
            new EntityKey(ValidationTestData.Assembly, 0x04000001),
            type,
            "Field",
            CliValueKind.I4,
            false);
        var fieldInstance = new FieldInstanceModel(
            field,
            CliTypeIdentity.Named(ValidationTestData.Assembly, "", "Unit", false),
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        Assert.NotEmpty(formatter.FormatOperand(new CilOperand.Entity(method.Key)));
        Assert.NotEmpty(formatter.FormatOperand(new CilOperand.TypeIdentity(
            CliTypeIdentity.FromStackKind(CliValueKind.I4))));
        Assert.NotEmpty(formatter.FormatOperand(new CilOperand.MethodInstance(
            ValidationTestData.Instance())));
        Assert.NotEmpty(formatter.FormatOperand(new CilOperand.FieldInstance(fieldInstance)));
        Assert.Throws<ArgumentNullException>(() => formatter.FormatOperand(null!));
    }

    [Fact]
    public void FormatsDispatchDeclarationsAndTargetsDeterministically()
    {
        var instance = ValidationTestData.Instance();
        var receiver = CliTypeIdentity.Named(
            ValidationTestData.Assembly,
            "",
            "Unit",
            isValueType: false);
        var site = new DispatchCallSiteModel(
            "caller",
            2,
            instance,
            [new DispatchTargetModel(receiver, instance)]);
        var program = ValidationTestData.Program() with
        {
            DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty
                .Add(site.Key, site),
        };

        var text = new CompilerDiagnosticDispatchTraceFormatter().FormatDispatch(program);

        Assert.Contains("[dispatch]", text);
        Assert.Contains("declaration=", text);
        Assert.Contains(" -> ", text);
        Assert.DoesNotContain('\r', text);
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticDispatchTraceFormatter().FormatDispatch(null!));
    }

    [Fact]
    public void TraceActorsRejectEveryMissingInputCapability()
    {
        using var context = CompilerInvariantTestContext.Create();
        var snapshot = context.Metadata.Snapshot;
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var types = MetadataCompilationTestActors.Types(snapshot);
        var fields = MetadataCompilationTestActors.Fields(snapshot);
        var typeFinder = MetadataCompilationTestActors.TypeFinder(snapshot);
        var definitions = MetadataCompilationTestActors.TypeDefinitions(snapshot);
        var identities = MetadataCompilationTestActors.TypeIdentities(snapshot);
        var instanceFields = CompilerTestSupport.CreateInstanceFields(context.Result.Layouts);
        var staticFields = CompilerTestSupport.CreateStaticFields(context.Result.Layouts);
        var typeLayouts = CompilerTestSupport.CreateTypeLayouts(definitions, context.Result.Layouts);
        var lowering = ValidationTestData.Lower(context.Result.Program);
        var layout = new CompilerDiagnosticLayoutTraceFormatter();

        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            null!, context.Result.Layouts, typeLayouts, instanceFields, staticFields,
            symbols, types, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, null!, typeLayouts, instanceFields, staticFields,
            symbols, types, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, context.Result.Layouts, null!, instanceFields, staticFields,
            symbols, types, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, context.Result.Layouts, typeLayouts, null!, staticFields,
            symbols, types, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, context.Result.Layouts, typeLayouts, instanceFields, null!,
            symbols, types, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, context.Result.Layouts, typeLayouts, instanceFields,
            staticFields, null!, types, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, context.Result.Layouts, typeLayouts, instanceFields,
            staticFields, symbols, null!, fields));
        Assert.Throws<ArgumentNullException>(() => layout.FormatLayouts(
            context.Result.Program, context.Result.Layouts, typeLayouts, instanceFields,
            staticFields, symbols, types, null!));

        var methods = new CompilerDiagnosticMethodTraceFormatter(
            new CompilerCilOperandFormatter(),
            new CompilerStructuredMethodFormatter());
        Assert.Throws<ArgumentNullException>(() => methods.FormatMethods(
            null!, context.Result.Program, lowering));
        Assert.Throws<ArgumentNullException>(() => methods.FormatMethods(
            symbols, null!, lowering));
        Assert.Throws<ArgumentNullException>(() => methods.FormatMethods(
            symbols, context.Result.Program, null!));

        var trace = new CompilerDiagnosticTraceFormatter(
            layout,
            new CompilerDiagnosticDispatchTraceFormatter(),
            methods);
        var entry = context.Result.Program.EntryPoint;
        Func<ISymbolFormatter?, ITypeRepository?, IFieldRepository?, ReachableProgram?,
            WasmMethodLoweringResult?, ManagedLayoutSnapshot?, ITypeLayoutProvider?,
            IInstanceFieldLayoutProvider?,
            IStaticFieldLayoutProvider?, string> invoke = (
            valueSymbols, valueTypes, valueFields, valueProgram, valueLowering, valueLayouts,
            valueTypeLayouts, valueInstanceFields, valueStaticFields) => trace.FormatTrace(
                valueSymbols!, valueTypes!, valueFields!, valueProgram!, valueLowering!, valueLayouts!,
                valueTypeLayouts!, valueInstanceFields!, valueStaticFields!, entry,
                WasmTarget.Wasm32, 0);
        Assert.Throws<ArgumentNullException>(() => invoke(
            null, types, fields, context.Result.Program, lowering, context.Result.Layouts,
            typeLayouts, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, null, fields, context.Result.Program, lowering, context.Result.Layouts,
            typeLayouts, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, null, context.Result.Program, lowering, context.Result.Layouts,
            typeLayouts, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, fields, null, lowering, context.Result.Layouts,
            typeLayouts, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, fields, context.Result.Program, null, context.Result.Layouts,
            typeLayouts, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, fields, context.Result.Program, lowering, null,
            typeLayouts, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, fields, context.Result.Program, lowering, context.Result.Layouts,
            null, instanceFields, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, fields, context.Result.Program, lowering, context.Result.Layouts,
            typeLayouts, null, staticFields));
        Assert.Throws<ArgumentNullException>(() => invoke(
            symbols, types, fields, context.Result.Program, lowering, context.Result.Layouts,
            typeLayouts, instanceFields, null));
        Assert.Contains("entry=none", trace.FormatTrace(
            symbols,
            types,
            fields,
            context.Result.Program,
            lowering,
            context.Result.Layouts,
            typeLayouts,
            instanceFields,
            staticFields,
            null!,
            WasmTarget.Wasm32,
            0));

        var traceWriter = new CompilerDiagnosticTraceWriter(
            trace,
            new RecordingTraceTextWriter());
        Assert.Throws<ArgumentException>(() => traceWriter.WriteTrace(
            "",
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            WasmTarget.Wasm32,
            0));
    }

    [Fact]
    public void LayoutTraceWritesReachableStaticFields()
    {
        using var context = CompilerInvariantTestContext.Create();
        var assembly = new AssemblyIdentity("LayoutDiagnostics");
        var declaringType = new EntityKey(assembly, 0x02000001);
        var fieldKey = new EntityKey(assembly, 0x04000001);
        var field = new FieldDefinitionModel(
            fieldKey,
            declaringType,
            "Root",
            CliValueKind.I4,
            true);
        var declaringIdentity = CliTypeIdentity.Named(
            assembly,
            "",
            "Root",
            isValueType: false);
        var staticConstructed = new FieldInstanceModel(
            new FieldDefinitionModel(
                new EntityKey(assembly, 0x04000002),
                declaringType,
                "Static",
                CliValueKind.I4,
                true),
            declaringIdentity,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var instanceConstructed = new FieldInstanceModel(
            new FieldDefinitionModel(
                new EntityKey(assembly, 0x04000003),
                declaringType,
                "Instance",
                CliValueKind.I4,
                false),
            declaringIdentity,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var program = context.Result.Program with
        {
            Fields = [fieldKey],
            ConstructedFields = ImmutableDictionary<string, FieldInstanceModel>.Empty
                .Add("static", staticConstructed)
                .Add("instance", instanceConstructed),
        };
        var text = new CompilerDiagnosticLayoutTraceFormatter().FormatLayouts(
            program,
            context.Result.Layouts,
            CompilerTestSupport.CreateTypeLayouts(
                MetadataCompilationTestActors.TypeDefinitions(context.Metadata.Snapshot),
                context.Result.Layouts),
            new MissingInstanceFields(),
            new FixedStaticFields(),
            MetadataCompilationTestActors.Symbols(context.Metadata.Snapshot),
            new FixedTypes(),
            new FixedFields(field));

        Assert.Contains("static-field", text);
        Assert.Contains("address=12", text);
        Assert.DoesNotContain('\r', text);
    }

    private sealed class RecordingTraceTextWriter : ICompilerDiagnosticTextArtifactWriter
    {
        public void WriteText(string path, string value) { }
    }

    private sealed class FixedTypes : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
            key,
            "",
            "RootType",
            false,
            [],
            []);
    }

    private sealed class FixedFields(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => field;
    }

    private sealed class FixedStaticFields : IStaticFieldLayoutProvider
    {
        public StaticFieldLayout GetStaticFieldLayout(EntityKey field) => new(12);

        public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) => new(12);
    }

    private sealed class MissingInstanceFields : IInstanceFieldLayoutProvider
    {
        public FieldLayout GetFieldLayout(EntityKey field) => new(0);

        public FieldLayout GetFieldLayout(FieldInstanceModel field) => new(0);
    }
}
