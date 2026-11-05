using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// A unit keeps the models its answers name; a later compile must rebuild exactly the
// models the compiler would have built, through the same factories.
public sealed class UnitModelCodecTests
{
    [Fact]
    public void EveryModelAProgramNamesRoundTrips()
    {
        using var assets = TestAssets.Create();
        var options = new CompilerOptions(assets.CompileOptimizedSource("ModelCodec", CompiledUnitRestoreTests.BaseSource),
            [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O0);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var program = services.GetRequiredService<INetWasmCompiler>().Compile(options).Program;
        using var lease = services.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath, options.ReferencePaths);
        var methods = services.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = services.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        var named = UnitQueryArguments.Of(program.Methods.Values.Concat(program.ConstructedMethods.Values));

        var writer = new UnitModelWriter();
        foreach (var type in named.Types.Values) writer.Type(type);
        foreach (var method in named.Methods.Values) writer.Method(method);
        foreach (var field in named.Fields.Values) writer.Field(field);
        var reader = new UnitModelReader(writer.ToArray(), methods, fields);

        Assert.True(named.Types.Count > 20 && named.Methods.Count > 20, $"the program names too few models: {named.Types.Count} types, {named.Methods.Count} methods");
        foreach (var type in named.Types.Values) AssertSame(type, reader.Type()!);
        foreach (var method in named.Methods.Values)
        {
            var read = reader.Method();
            Assert.Equal(method.CanonicalName, read.CanonicalName);
            Assert.Equal(method.Definition.Key, read.Definition.Key);
            AssertSame(method.Signature, read.Signature, method.CanonicalName);
        }
        foreach (var field in named.Fields.Values)
        {
            var read = reader.Field();
            Assert.Equal(field.CanonicalName, read.CanonicalName);
            AssertSame(field.FieldType, read.FieldType);
        }
        Assert.True(reader.AtEnd);
    }

    private static void AssertSame(CliTypeIdentity expected, CliTypeIdentity actual)
    {
        Assert.Equal(expected.CanonicalName, actual.CanonicalName);
        var context = expected.CanonicalName;
        Assert.True(expected.Shape == actual.Shape, context);
        Assert.True(expected.StackKind == actual.StackKind, context);
        Assert.True(expected.IsValueType == actual.IsValueType, context);
        Assert.True(expected.Assembly == actual.Assembly, context);
        Assert.True(expected.FullName == actual.FullName, context);
        Assert.True(expected.GenericParameterIndex == actual.GenericParameterIndex, context);
        Assert.True(expected.ArrayRank == actual.ArrayRank, context);
        Assert.True(expected.IsRequiredModifier == actual.IsRequiredModifier, context);
        Assert.True(Equals(expected.StackStorageType, actual.StackStorageType), context);
        Assert.True(Equals(expected.ElementType, actual.ElementType), context);
        Assert.True(Equals(expected.CustomModifier, actual.CustomModifier), context);
        Assert.True(expected.TypeArguments.SequenceEqual(actual.TypeArguments), context);
        Assert.True(Equals(expected.FunctionPointerSignature?.Signature.ReturnSignatureType,
            actual.FunctionPointerSignature?.Signature.ReturnSignatureType), context);
    }

    private static void AssertSame(MethodSignatureModel expected, MethodSignatureModel actual, string context)
    {
        Assert.True(expected.ReturnType == actual.ReturnType, context);
        Assert.True(expected.ParameterTypes.SequenceEqual(actual.ParameterTypes), context);
        Assert.True(Equals(expected.ReturnSignatureType, actual.ReturnSignatureType), context);
        Assert.True(expected.ParameterSignatureTypes.SequenceEqual(actual.ParameterSignatureTypes), context);
    }
}
