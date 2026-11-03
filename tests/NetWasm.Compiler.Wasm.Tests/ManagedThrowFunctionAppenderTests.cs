using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedThrowFunctionAppenderTests
{
    [Fact]
    public void MissingPlanAppendsNothing()
    {
        var functions = new List<WasmFunctionDefinition>();

        CreateAppender().Append(
            functions,
            OptionalFunctionIndex.Missing,
            OptionalFunctionIndex.Missing,
            [],
            new Indices());

        Assert.Empty(functions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentPlansAppendNonInlineableThrowFunctionsInIndexOrder(bool rethrow)
    {
        var functions = new List<WasmFunctionDefinition>();
        var imports = WasmRuntimeImports.CreateCatalog();

        CreateAppender(imports).Append(
            functions,
            OptionalFunctionIndex.At(37),
            rethrow ? OptionalFunctionIndex.At(38) : OptionalFunctionIndex.Missing,
            [],
            new Indices());

        Assert.Equal(
            rethrow ? ["managed.throw", "managed.rethrow"] : ["managed.throw"],
            functions.Select(static function => function.Name));
        AssertThrowFunction(functions[0], imports.Resolve(RuntimeImportSymbol.BeginThrow));
        if (rethrow)
        {
            AssertThrowFunction(functions[1], imports.Resolve(RuntimeImportSymbol.BeginRethrow));
        }
    }

    [Fact]
    public void ConstructorAndAppenderRejectMissingDependencies()
    {
        var imports = WasmRuntimeImports.CreateCatalog();

        var writers = new GeneratedFunctionWriterFactory();
        var layouts = new RecordingLayoutProvider();
        var program = new Program([]);
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(null!, imports, layouts, program, program));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(writers, null!, layouts, program, program));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(writers, imports, null!, program, program));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(writers, imports, layouts, null!, program));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedThrowFunctionAppender(writers, imports, layouts, program, null!));
        Assert.Throws<ArgumentNullException>(() =>
            CreateAppender(imports).Append(
                null!,
                OptionalFunctionIndex.At(0),
                OptionalFunctionIndex.Missing,
                [],
                new Indices()));
        Assert.Throws<ArgumentNullException>(() =>
            CreateAppender(imports).Append(
                [],
                OptionalFunctionIndex.At(0),
                OptionalFunctionIndex.Missing,
                [],
                null!));
    }

    // A check passes its kind's tag; managed.throw calls the CoreLib getter for
    // it and throws the exception the getter returns, and throws any other
    // argument as it is. A method of the getter's name on another type is no
    // getter.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowReplacesTagsWithTheirGettersExceptions(bool wasm64)
    {
        var helpers = new EntityKey(Assembly, 0x02000010);
        var other = new EntityKey(Assembly, 0x02000011);
        MethodDefinitionModel Method(int token, EntityKey type, string name) =>
            new(new(Assembly, token), type, name, true, new(CliValueKind.Void, []), 1);
        var methods = new[]
        {
            Method(0x06000010, helpers, "GetIndexOutOfRangeException"),
            Method(0x06000011, other, "GetNullReferenceException"),
            Method(0x06000012, helpers, "GetNullReferenceException"),
            Method(0x06000013, helpers, "Unrelated"),
        };
        var program = new Program(methods)
        {
            Types =
            {
                [helpers] = new(helpers, "System", "RuntimeExceptionHelpers", false, [], []),
                [other] = new(other, "Example", "RuntimeExceptionHelpers", false, [], []),
            },
        };
        var imports = WasmRuntimeImports.CreateCatalog();
        var functions = new List<WasmFunctionDefinition>();

        new ManagedThrowFunctionAppender(
            new GeneratedFunctionWriterFactory(),
            imports,
            new RecordingLayoutProvider(wasm64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32),
            program,
            program).Append(
                functions,
                OptionalFunctionIndex.At(37),
                OptionalFunctionIndex.At(38),
                [.. methods.Select(static method => new ManagedDefinitionEmission(default!, method.Key))],
                new Indices { [new(Assembly, 0x06000010)] = 51, [new(Assembly, 0x06000012)] = 50 });

        byte constant = wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant;
        Assert.Equal(
            [
                0,
                WasmOpcodes.LocalGet, 0, constant, 1, wasm64 ? WasmOpcodes.I64Subtract : WasmOpcodes.I32Subtract,
                constant, RuntimeExceptionGetters.TagLimit - 1,
                wasm64 ? WasmOpcodes.I64LessThanUnsigned : WasmOpcodes.I32LessThanUnsigned,
                WasmOpcodes.If, WasmOpcodes.EmptyBlockType,
                WasmOpcodes.LocalGet, 0, constant, (byte)RuntimeExceptionGetters.Tag(ManagedExceptionKind.NullReference),
                wasm64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal, WasmOpcodes.If, WasmOpcodes.EmptyBlockType,
                WasmOpcodes.Call, 50, WasmOpcodes.LocalSet, 0, WasmOpcodes.Branch, 1, WasmOpcodes.End,
                WasmOpcodes.LocalGet, 0, constant, (byte)RuntimeExceptionGetters.Tag(ManagedExceptionKind.IndexOutOfRange),
                wasm64 ? WasmOpcodes.I64Equal : WasmOpcodes.I32Equal, WasmOpcodes.If, WasmOpcodes.EmptyBlockType,
                WasmOpcodes.Call, 51, WasmOpcodes.LocalSet, 0, WasmOpcodes.Branch, 1, WasmOpcodes.End,
                WasmOpcodes.Unreachable, WasmOpcodes.End,
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Call, (byte)imports.Resolve(RuntimeImportSymbol.BeginThrow),
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Throw, 0,
                WasmOpcodes.End,
            ],
            functions[0].Body);
        AssertThrowFunction(functions[1], imports.Resolve(RuntimeImportSymbol.BeginRethrow));
    }

    private static void AssertThrowFunction(WasmFunctionDefinition function, int beginImport)
    {
        Assert.Equal(CliValueKind.Void, function.Type.Result);
        Assert.Equal(
            [CliValueKind.ManagedReference],
            function.Type.Parameters.ToArray());
        Assert.True(function.IsNoInlining);
        Assert.False(function.IsAggressiveInlining);
        Assert.Equal(
            [
                0,
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Call, (byte)beginImport,
                WasmOpcodes.LocalGet, 0,
                WasmOpcodes.Throw, 0,
                WasmOpcodes.End,
            ],
            function.Body);
    }

    private static readonly AssemblyIdentity Assembly = EmitterTestSupport.EntryKey.Assembly;

    private static ManagedThrowFunctionAppender CreateAppender(
        IRuntimeImportResolver? imports = null)
    {
        var program = new Program([]);
        return new ManagedThrowFunctionAppender(
            new GeneratedFunctionWriterFactory(),
            imports ?? WasmRuntimeImports.CreateCatalog(),
            new RecordingLayoutProvider(),
            program,
            program);
    }

    private sealed class Program(IEnumerable<MethodDefinitionModel> methods) : IMethodRepository, ITypeRepository
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = methods.ToDictionary(static method => method.Key);

        public Dictionary<EntityKey, TypeDefinitionModel> Types { get; } = [];

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => Types[key];
    }

    private sealed class Indices : Dictionary<EntityKey, int>, IFunctionIndexResolver
    {
        public OptionalFunctionIndex ManagedThrowFunction => OptionalFunctionIndex.At(37);

        public int Resolve(EntityKey method) => this[method];
        public int Resolve(string method) => throw new NotSupportedException();
        public int Resolve(MethodInstanceModel method) => throw new NotSupportedException();
        public int Resolve(InteropImportKey import) => throw new NotSupportedException();
    }
}
