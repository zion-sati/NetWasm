using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class EmittedTupleElementNamesAttributeTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void NullTransformNamesThrowAcrossCompilerProfilesAndTargets()
    {
        var fixture = new CorpusFixture(
            "TupleElementNamesAttributeCompatibility",
            "NetWasm.Correctness.TupleElementNamesAttributeCompatibility",
            string.Empty,
            [0])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            CaptureCompilerDiagnostics = true,
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add(
                "System.Private.CoreLib",
                "NetWasm.CoreLib"),
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty.Add(0, 500),
        };

        runner.Run(fixture, new TupleElementNamesAttributeFixtureBuilder());
    }
}

// C# reserves TupleElementNamesAttribute for compiler use, so its constructor
// contract must be authored as programmatic CIL rather than C# source.
internal sealed class TupleElementNamesAttributeFixtureBuilder : IEmittedAssemblyBuilder
{
    private static readonly ConstructorInfo Constructor = typeof(object).Assembly.GetType(
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        throwOnError: true)!.GetConstructor([typeof(string[])])!;

    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("TupleElementNamesAttributeCompatibility"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("TupleElementNamesAttributeCompatibility.dll");
        var type = module.DefineType(
            "NetWasm.Correctness.TupleElementNamesAttributeCompatibility.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        DefineRun(type);
        DefineTrace(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return [.. stream.ToArray()];
    }

    private static void DefineRun(TypeBuilder type)
    {
        var method = type.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var code = method.GetILGenerator();
        var result = code.DeclareLocal(typeof(int));

        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldnull);
        code.Emit(OpCodes.Newobj, Constructor);
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4, -500);
        code.Emit(OpCodes.Stloc, result);
        code.BeginCatchBlock(typeof(ArgumentNullException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4, 500);
        code.Emit(OpCodes.Stloc, result);
        code.EndExceptionBlock();
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
    }

    private static void DefineTrace(TypeBuilder type)
    {
        var method = type.DefineMethod(
            "Trace",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ret);
    }
}
