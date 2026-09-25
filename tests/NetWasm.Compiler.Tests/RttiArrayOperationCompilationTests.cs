using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

[Collection(CorrectnessTestGroup.Name)]
public sealed class RttiArrayOperationCompilationTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    public static TheoryData<string, string> ZeroBoundCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ZeroBound);

    public static TheoryData<string, string> NonZeroBoundCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NonZeroBound);

    public static TheoryData<int, string, string> RankOneCases => CorpusCaseTestData.ActiveInputCells(CorpusCaseTestData.RankOne);

    [Fact]
    public void EmittedFixturePreservesRankOneMetadataAndManagedCatchShape()
    {
        var builder = Assert.IsAssignableFrom<IEmittedAssemblyBuilder>(new RankOneArrayFixtureBuilder());
        using var stream = new MemoryStream(builder.Build().ToArray());
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var signatures = Enumerable.Range(1, metadata.GetTableRowCount(TableIndex.TypeSpec))
            .Select(row => metadata.GetBlobBytes(metadata.GetTypeSpecification(
                MetadataTokens.TypeSpecificationHandle(row)).Signature))
            .ToArray();

        foreach (var elementType in new byte[] { 0x08, 0x0e, 0x1c })
        {
            // ARRAY, element type, rank 1, one size (1), one lower bound (0).
            byte[] expected = [0x14, elementType, 0x01, 0x01, 0x01, 0x01, 0x00];
            Assert.Contains(signatures, signature => signature.AsSpan().SequenceEqual(expected));
        }
        Assert.Contains(signatures, signature => signature.AsSpan().SequenceEqual<byte>([0x1d, 0x08]));
        Assert.DoesNotContain(signatures, signature => signature.Length > 2 && signature[0] == 0x14 && signature[2] != 1);

        var method = Assert.Single(metadata.MethodDefinitions.Select(metadata.GetMethodDefinition),
            definition => metadata.GetString(definition.Name) == "RejectedMutableAddress");
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        Assert.False(body.LocalSignature.IsNil);
        var handler = Assert.Single(body.ExceptionRegions);
        Assert.Equal(ExceptionRegionKind.Catch, handler.Kind);
        Assert.Equal(HandleKind.TypeReference, handler.CatchType.Kind);
        var exceptionType = metadata.GetTypeReference((TypeReferenceHandle)handler.CatchType);
        Assert.Equal("System", metadata.GetString(exceptionType.Namespace));
        Assert.Equal("ArrayTypeMismatchException", metadata.GetString(exceptionType.Name));
    }

    [Theory]
    [MemberData(nameof(RankOneCases))]
    public void CompilerExecutesDistinctSzAndRankOneArraySemantics(int input, string caseId, string cell)
    {
        // Input 2 addresses a normalized SZARRAY through an ARRAY token.
        // CoreCLR's mutable Address stub requires exact array identity.
        Run(CorpusCaseTestData.Get(caseId), cell, new RankOneArrayFixtureBuilder(), input);
    }

    [Fact(Skip = KnownNonBugSkipReasons.ZeroBoundRankOneArrayIdentity)]
    public void CoreClrZeroBoundRankOneNormalizationIsNotRequired() =>
        throw new InvalidOperationException("The known non-bug partition must remain skipped.");

    [Theory(Skip = KnownNonBugSkipReasons.ZeroBoundRankOneArrayIdentity)]
    [MemberData(nameof(ZeroBoundCells))]
    public void CompilerPreservesZeroBasedRankOneArrayIdentity(string caseId, string cell)
    {
        // CoreCLR AllocateArrayEx normalizes rank-one, zero-lower-bound
        // construction to SZARRAY, even when the constructor token is ARRAY.
        // Keep this minimal witness separate from the broader shape/access
        // regression. Both must fail normally if normalization is missing.
        Run(CorpusCaseTestData.Get(caseId), cell, new RankOneArrayFixtureBuilder());
    }

    [Theory]
    [MemberData(nameof(NonZeroBoundCells))]
    public void CompilerPreservesMutableAddressForNonZeroBoundRankOneArray(string caseId, string cell)
    {
        Run(CorpusCaseTestData.Get(caseId), cell, new RankOneArrayFixtureBuilder(nonZeroBoundOnly: true));
    }


    private sealed class RankOneArrayFixtureBuilder(bool nonZeroBoundOnly = false) : IEmittedAssemblyBuilder
    {
        public ImmutableArray<byte> Build() => CreateRankOneArrayFixture(nonZeroBoundOnly);
    }

    private static ImmutableArray<byte> CreateRankOneArrayFixture(bool nonZeroBoundOnly)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("RttiRankOneArray"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("RttiRankOneArray.dll");
        var entryPoint = module.DefineType(
            "RttiRankOneArray.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        // Keep the new constructor shape in its own PE: a compilation rejection
        // in that control must not prevent execution of the original witnesses.
        MethodBuilder[] cases = nonZeroBoundOnly
            ? [DefineExactMutableAddress(module, entryPoint, nonZeroBound: true)]
            :
        [
            DefineRankOneGetSet(module, entryPoint),
            DefineArrayShapeCasts(module, entryPoint),
            DefineExactMutableAddress(module, entryPoint, nonZeroBound: false),
            DefineRejectedMutableAddress(module, entryPoint),
            DefineReadonlyCovariantAddress(module, entryPoint),
            DefineZeroBasedArrayIdentity(module, entryPoint),
        ];
        DefineDispatcher(entryPoint, cases);
        entryPoint.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        var image = stream.ToArray();
        if (nonZeroBoundOnly)
        {
            RewriteArraySignature(image, 0x0e);
        }
        else
        {
            RewriteRankTwoArraySignaturesAsRankOne(image);
        }
        return [.. image];
    }

    private static MethodBuilder DefineRankOneGetSet(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "RankOneGetSet");
        var code = method.GetILGenerator();
        var arrayType = typeof(int).MakeArrayType(2);
        var array = code.DeclareLocal(arrayType);

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            arrayType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Stloc, array);
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Set",
            typeof(void),
            typeof(int),
            typeof(int)));
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Get",
            typeof(int),
            typeof(int)));
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineZeroBasedArrayIdentity(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "ZeroBasedArrayIdentity");
        var code = method.GetILGenerator();
        var failed = code.DefineLabel();
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module, typeof(int).MakeArrayType(2), ".ctor", typeof(void), typeof(int)));
        code.Emit(OpCodes.Isinst, typeof(int[]));
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(failed);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineArrayShapeCasts(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "ArrayShapeCasts");
        var code = method.GetILGenerator();
        var rankOneType = typeof(int).MakeArrayType(2);
        var szType = typeof(int[]);
        var rankOne = code.DeclareLocal(rankOneType);
        var sz = code.DeclareLocal(szType);
        var failed = code.DefineLabel();

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            rankOneType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Stloc, rankOne);
        code.Emit(OpCodes.Ldloc, rankOne);
        code.Emit(OpCodes.Isinst, rankOneType);
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldloc, rankOne);
        code.Emit(OpCodes.Isinst, szType);
        // A rank-one ARRAY constructor with an implicit zero lower bound
        // produces SZARRAY on CoreCLR. Metadata shape is not object identity.
        code.Emit(OpCodes.Brfalse, failed);

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newarr, typeof(int));
        code.Emit(OpCodes.Stloc, sz);
        code.Emit(OpCodes.Ldloc, sz);
        code.Emit(OpCodes.Isinst, szType);
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldloc, sz);
        code.Emit(OpCodes.Isinst, rankOneType);
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);

        code.MarkLabel(failed);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineExactMutableAddress(ModuleBuilder module, TypeBuilder type, bool nonZeroBound)
    {
        var method = DefineCase(type, nonZeroBound ? "NonZeroBoundExactMutableAddress" : "ExactMutableAddress");
        var code = method.GetILGenerator();
        var arrayType = typeof(string).MakeArrayType(2);
        var array = code.DeclareLocal(arrayType);
        var succeeded = code.DefineLabel();

        if (nonZeroBound)
        {
            code.Emit(OpCodes.Ldc_I4_1); // lower bound, followed by length
        }
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            arrayType,
            ".ctor",
            typeof(void),
            nonZeroBound ? [typeof(int), typeof(int)] : [typeof(int)]));
        code.Emit(OpCodes.Stloc, array);
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(nonZeroBound ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Address",
            typeof(string).MakeByRefType(),
            typeof(int)));
        if (nonZeroBound)
        {
            code.Emit(OpCodes.Ldstr, "stored-through-address");
        }
        else
        {
            code.Emit(OpCodes.Ldnull);
        }
        code.Emit(OpCodes.Stind_Ref);
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(nonZeroBound ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Get",
            typeof(string),
            typeof(int)));
        if (nonZeroBound)
        {
            code.Emit(OpCodes.Ldstr, "stored-through-address");
            code.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
            code.Emit(OpCodes.Brtrue, succeeded);
        }
        else
        {
            code.Emit(OpCodes.Brfalse, succeeded);
        }
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(succeeded);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineRejectedMutableAddress(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "RejectedMutableAddress");
        var code = method.GetILGenerator();
        var sourceType = typeof(string).MakeArrayType(2);
        var viewType = typeof(object).MakeArrayType(2);
        var view = code.DeclareLocal(viewType);
        var result = code.DeclareLocal(typeof(int));
        var complete = code.DefineLabel();

        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            sourceType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Castclass, viewType);
        code.Emit(OpCodes.Stloc, view);
        code.Emit(OpCodes.Ldloc, view);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            viewType,
            "Address",
            typeof(object).MakeByRefType(),
            typeof(int)));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Stloc, result);
        code.Emit(OpCodes.Leave, complete);
        code.BeginCatchBlock(typeof(ArrayTypeMismatchException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Stloc, result);
        code.Emit(OpCodes.Leave, complete);
        code.EndExceptionBlock();
        code.MarkLabel(complete);
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineReadonlyCovariantAddress(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "ReadonlyCovariantAddress");
        var code = method.GetILGenerator();
        var sourceType = typeof(string).MakeArrayType(2);
        var viewType = typeof(object).MakeArrayType(2);
        var view = code.DeclareLocal(viewType);
        var succeeded = code.DefineLabel();

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            sourceType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Castclass, viewType);
        code.Emit(OpCodes.Stloc, view);
        code.Emit(OpCodes.Ldloc, view);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Readonly);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            viewType,
            "Address",
            typeof(object).MakeByRefType(),
            typeof(int)));
        code.Emit(OpCodes.Ldind_Ref);
        code.Emit(OpCodes.Brfalse, succeeded);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(succeeded);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static void DefineDispatcher(TypeBuilder type, MethodBuilder[] cases)
    {
        var method = type.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var code = method.GetILGenerator();
        var labels = cases.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < cases.Length; index++)
        {
            code.MarkLabel(labels[index]);
            code.Emit(OpCodes.Call, cases[index]);
            code.Emit(OpCodes.Ret);
        }
    }

    private static MethodBuilder DefineCase(TypeBuilder type, string name) =>
        type.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);

    private static MethodInfo DefineArrayMethod(
        ModuleBuilder module,
        Type arrayType,
        string name,
        Type returnType,
        params Type[] parameterTypes) => module.GetArrayMethod(
        arrayType,
        name,
        CallingConventions.HasThis,
        returnType,
        parameterTypes);

    private static void RewriteRankTwoArraySignaturesAsRankOne(byte[] image)
    {
        RewriteArraySignature(image, 0x08);
        RewriteArraySignature(image, 0x0e);
        RewriteArraySignature(image, 0x1c);
    }

    private static void RewriteArraySignature(byte[] image, byte elementType)
    {
        ReadOnlySpan<byte> rankTwo = [0x14, elementType, 0x02, 0x00, 0x02, 0x00, 0x00];
        ReadOnlySpan<byte> rankOne = [0x14, elementType, 0x01, 0x01, 0x01, 0x01, 0x00];
        var rewritten = 0;
        for (var offset = 0; offset <= image.Length - rankTwo.Length; offset++)
        {
            if (!image.AsSpan(offset, rankTwo.Length).SequenceEqual(rankTwo))
            {
                continue;
            }

            rankOne.CopyTo(image.AsSpan(offset, rankOne.Length));
            rewritten++;
        }

        if (rewritten == 0)
        {
            throw new InvalidOperationException(
                $"The persisted fixture has no rank-two signature for element type 0x{elementType:x2}.");
        }
    }
}
