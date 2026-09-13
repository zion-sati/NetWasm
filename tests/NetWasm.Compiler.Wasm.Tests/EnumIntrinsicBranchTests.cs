using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class EnumIntrinsicBranchTests
{
    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Equal)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Equal)]
    public void GetNameEmitsTheUnderlyingWidthComparison(
        string underlyingName,
        CliValueKind stackKind,
        byte comparison)
    {
        var metadata = new EnumMetadataFixture(CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalGetName",
            RuntimeIntrinsic.EnumGetName,
            CliValueKind.ManagedReference,
            [stackKind],
            [stackKind],
            [metadata.EnumType]);
        var code = new RecordingInstructionWriter();

        CreateGetNameEmitter(metadata)
            .EmitGetName(request, code);

        Assert.Contains(comparison, code.ToArray());
    }

    [Fact]
    public void GetNameRejectsAnUnsupportedMethodArgumentShape()
    {
        var metadata = new EnumMetadataFixture(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalGetName",
            RuntimeIntrinsic.EnumGetName,
            CliValueKind.ManagedReference,
            [CliValueKind.I4, CliValueKind.I4],
            [CliValueKind.I4, CliValueKind.I4],
            [metadata.EnumType, metadata.EnumType]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateGetNameEmitter(metadata)
                .EmitGetName(request, new RecordingInstructionWriter()));

        Assert.Contains("one enum type argument", exception.Message);
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Equal)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Equal)]
    public void GetNameSupportsTheTypeBasedContract(
        string underlyingName,
        CliValueKind stackKind,
        byte comparison)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalGetName",
            RuntimeIntrinsic.EnumGetName,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        CreateGetNameEmitter(metadata)
            .EmitGetName(request, code);

        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
        Assert.Contains(comparison, code.ToArray());
    }

    [Fact]
    public void GetNameRejectsMetadataThatIsUnavailable()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var unknown = CliTypeIdentity.Named(
            TestAssembly,
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);
        var request = Request(
            "InternalGetName",
            RuntimeIntrinsic.EnumGetName,
            CliValueKind.ManagedReference,
            [CliValueKind.I4],
            [CliValueKind.I4],
            [unknown]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateGetNameEmitter(metadata)
                .EmitGetName(request, new RecordingInstructionWriter()));

        Assert.Contains("metadata is unavailable", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetNamesSupportsGenericAndTypeBasedContracts(bool typeBased)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var stack = typeBased
            ? new[] { CliValueKind.ManagedReference }
            : Array.Empty<CliValueKind>();
        ImmutableArray<CliTypeIdentity> args = typeBased ? [] : [metadata.EnumType];
        var request = Request(
            "InternalGetNames",
            RuntimeIntrinsic.EnumGetNames,
            CliValueKind.ManagedReference,
            typeBased ? [CliValueKind.ManagedReference] : [],
            stack,
            args);
        var code = new RecordingInstructionWriter();

        CreateGetNamesEmitter(metadata)
            .EmitGetNames(request, code);

        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Fact]
    public void GetNamesRejectsMoreThanOneClosedEnumArgument()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalGetNames",
            RuntimeIntrinsic.EnumGetNames,
            CliValueKind.ManagedReference,
            [CliValueKind.I4, CliValueKind.I4],
            [CliValueKind.I4, CliValueKind.I4],
            [metadata.EnumType, metadata.EnumType]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateGetNamesEmitter(metadata)
                .EmitGetNames(request, new RecordingInstructionWriter()));

        Assert.Contains("one closed enum type argument", exception.Message);
    }

    [Fact]
    public void GetNamesRejectsMetadataFromAnotherAssembly()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var unknown = CliTypeIdentity.Named(
            new AssemblyIdentity("other-enum-assembly"),
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);
        var request = Request(
            "InternalGetNames",
            RuntimeIntrinsic.EnumGetNames,
            CliValueKind.ManagedReference,
            [],
            [],
            [unknown]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateGetNamesEmitter(metadata)
                .EmitGetNames(request, new RecordingInstructionWriter()));

        Assert.Contains("metadata is unavailable", exception.Message);
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Store)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Store)]
    public void GetValuesStoresTheUnderlyingWidth(
        string underlyingName,
        CliValueKind stackKind,
        byte store)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalGetValues",
            RuntimeIntrinsic.EnumGetValues,
            CliValueKind.ManagedReference,
            [],
            [],
            [metadata.EnumType]);
        var code = new RecordingInstructionWriter();

        CreateGetValuesEmitter(metadata)
            .EmitGetValues(request, code);

        Assert.Contains(store, code.ToArray());
    }

    [Fact]
    public void GetValuesAsUnderlyingTypeUsesThePrimitiveElementType()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalGetValuesAsUnderlyingType",
            RuntimeIntrinsic.EnumGetValues,
            CliValueKind.ManagedReference,
            [],
            [],
            [metadata.EnumType]);
        var code = new RecordingInstructionWriter();

        CreateGetValuesEmitter(metadata)
            .EmitGetValues(request, code);

        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
    }

    [Fact]
    public void GetValuesTypeBasedFallsBackToTheReferenceArrayLayout()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            fallbackArrayLayout: true);
        var request = Request(
            "InternalGetValues",
            RuntimeIntrinsic.EnumGetValues,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4]);
        var code = new RecordingInstructionWriter();

        CreateGetValuesEmitter(metadata)
            .EmitGetValues(request, code);

        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Store)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Store)]
    public void GetValuesSupportsTheTypeBasedContract(
        string underlyingName,
        CliValueKind stackKind,
        byte store)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalGetValues",
            RuntimeIntrinsic.EnumGetValues,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, stackKind],
            [CliValueKind.ManagedReference, stackKind]);
        var code = new RecordingInstructionWriter();

        CreateGetValuesEmitter(metadata)
            .EmitGetValues(request, code);

        Assert.Contains(store, code.ToArray());
    }

    [Fact]
    public void GetValuesRejectsMoreThanOneClosedEnumArgument()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalGetValues",
            RuntimeIntrinsic.EnumGetValues,
            CliValueKind.ManagedReference,
            [CliValueKind.I4, CliValueKind.I4],
            [CliValueKind.I4, CliValueKind.I4],
            [metadata.EnumType, metadata.EnumType]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateGetValuesEmitter(metadata)
                .EmitGetValues(request, new RecordingInstructionWriter()));

        Assert.Contains("one enum type argument", exception.Message);
    }

    [Fact]
    public void GetValuesRejectsMetadataThatIsUnavailable()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var unknown = CliTypeIdentity.Named(
            TestAssembly,
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);
        var request = Request(
            "InternalGetValues",
            RuntimeIntrinsic.EnumGetValues,
            CliValueKind.ManagedReference,
            [],
            [],
            [unknown]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateGetValuesEmitter(metadata)
                .EmitGetValues(request, new RecordingInstructionWriter()));

        Assert.Contains("metadata is unavailable", exception.Message);
    }

    [Fact]
    public void UnderlyingTypePublishesATypeObjectForEachMetadataEntry()
    {
        var underlying = CliTypeIdentity.Primitive("u2", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalGetUnderlyingType",
            RuntimeIntrinsic.EnumGetUnderlyingType,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        CreateGetUnderlyingTypeEmitter(metadata)
            .EmitGetUnderlyingType(request, code);

        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Theory]
    [InlineData("i4", WasmOpcodes.I32Equal)]
    [InlineData("i8", WasmOpcodes.I64Equal)]
    public void IsDefinedSupportsTheTypeBasedContract(
        string underlyingName,
        byte comparison)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(
                underlyingName,
                underlyingName == "i8" ? CliValueKind.I8 : CliValueKind.I4));
        var request = Request(
            "InternalIsDefined",
            RuntimeIntrinsic.EnumIsDefined,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumIsDefinedEmitter(
                metadata, metadata, metadata.Layouts, metadata.Layouts, metadata)
            .EmitIsDefined(request, code);

        Assert.Contains(comparison, code.ToArray());
    }

    [Fact]
    public void IsDefinedRejectsMoreThanOneClosedEnumArgument()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalIsDefined",
            RuntimeIntrinsic.EnumIsDefined,
            CliValueKind.I4,
            [CliValueKind.I4, CliValueKind.I4],
            [CliValueKind.I4, CliValueKind.I4],
            [metadata.EnumType, metadata.EnumType]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumIsDefinedEmitter(
                    metadata, metadata, metadata.Layouts, metadata.Layouts, metadata)
                .EmitIsDefined(request, new RecordingInstructionWriter()));

        Assert.Contains("one enum type argument", exception.Message);
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Equal)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Equal)]
    public void IsDefinedSupportsAClosedEnumArgument(
        string underlyingName,
        CliValueKind stackKind,
        byte comparison)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalIsDefined",
            RuntimeIntrinsic.EnumIsDefined,
            CliValueKind.I4,
            [stackKind],
            [stackKind],
            [metadata.EnumType]);
        var code = new RecordingInstructionWriter();

        new EnumIsDefinedEmitter(
                metadata, metadata, metadata.Layouts, metadata.Layouts, metadata)
            .EmitIsDefined(request, code);

        Assert.Contains(comparison, code.ToArray());
    }

    [Fact]
    public void IsDefinedRejectsMetadataThatIsUnavailable()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var unknown = CliTypeIdentity.Named(
            TestAssembly,
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);
        var request = Request(
            "InternalIsDefined",
            RuntimeIntrinsic.EnumIsDefined,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4],
            [unknown]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumIsDefinedEmitter(
                    metadata, metadata, metadata.Layouts, metadata.Layouts, metadata)
                .EmitIsDefined(request, new RecordingInstructionWriter()));

        Assert.Contains("metadata is unavailable", exception.Message);
    }

    [Theory]
    [InlineData("i1", CliValueKind.I4, WasmOpcodes.I32Constant)]
    [InlineData("i2", CliValueKind.I4, WasmOpcodes.I32Constant)]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Constant)]
    [InlineData("u4", CliValueKind.I4, WasmOpcodes.I32Constant)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Constant)]
    public void ParseEmitsWidthAppropriateEnumConstants(
        string underlyingName,
        CliValueKind stackKind,
        byte constant)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalParse",
            RuntimeIntrinsic.EnumParse,
            stackKind,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [metadata.EnumType]);
        var code = new RecordingInstructionWriter();

        new EnumParseEmitter(
                metadata,
                metadata,
                metadata.Layouts,
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                new EnumNumericParseEmitter(
                    metadata.Layouts,
                    new AddressInstructionEmitter(metadata.Layouts),
                    metadata.Layouts),
                metadata,
                CreateEnumValueBoxEmitter(metadata))
            .EmitParse(request, code);

        Assert.Contains(constant, code.ToArray());
    }

    [Fact]
    public void ParseRejectsAnUnclosedEnumMethodArgument()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalParse",
            RuntimeIntrinsic.EnumParse,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumParseEmitter(
                    metadata,
                    metadata,
                    metadata.Layouts,
                    new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                    new AddressInstructionEmitter(metadata.Layouts),
                    metadata.Layouts,
                    new EnumNumericParseEmitter(
                        metadata.Layouts,
                        new AddressInstructionEmitter(metadata.Layouts),
                        metadata.Layouts),
                    metadata,
                    CreateEnumValueBoxEmitter(metadata))
                .EmitParse(request, new RecordingInstructionWriter()));

        Assert.Contains("one enum type argument", exception.Message);
    }

    [Fact]
    public void ParseRejectsMultipleEnumMethodArguments()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalParse",
            RuntimeIntrinsic.EnumParse,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [metadata.EnumType, metadata.EnumType]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEnumParseEmitter(metadata).EmitParse(
                request,
                new RecordingInstructionWriter()));

        Assert.Contains("one enum type argument", exception.Message);
    }

    [Theory]
    [InlineData("i1", CliValueKind.I4, WasmOpcodes.I32Store8)]
    [InlineData("u2", CliValueKind.I4, WasmOpcodes.I32Store16)]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Store)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Store)]
    public void TryParseStoresTheOutValueAndFailureResult(
        string underlyingName,
        CliValueKind stackKind,
        byte expectedStore)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind),
            flags: true);
        var request = Request(
            "InternalTryParse",
            RuntimeIntrinsic.EnumParse,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedAddress],
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedAddress],
            [metadata.EnumType]);
        var code = new RecordingInstructionWriter();

        new EnumParseEmitter(
                metadata,
                metadata,
                metadata.Layouts,
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                new EnumNumericParseEmitter(
                    metadata.Layouts,
                    new AddressInstructionEmitter(metadata.Layouts),
                    metadata.Layouts),
                metadata,
                CreateEnumValueBoxEmitter(metadata))
            .EmitParse(request, code);

        Assert.Contains(expectedStore, code.ToArray());
        Assert.Contains(WasmOpcodes.I32EqualZero, code.ToArray());
    }

    [Fact]
    public void ParseRejectsMetadataThatIsUnavailable()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var unknown = CliTypeIdentity.Named(
            TestAssembly,
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);
        var request = Request(
            "InternalParse",
            RuntimeIntrinsic.EnumParse,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [unknown]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumParseEmitter(
                    metadata,
                    metadata,
                    metadata.Layouts,
                    new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                    new AddressInstructionEmitter(metadata.Layouts),
                    metadata.Layouts,
                    new EnumNumericParseEmitter(
                        metadata.Layouts,
                        new AddressInstructionEmitter(metadata.Layouts),
                        metadata.Layouts),
                    metadata,
                    CreateEnumValueBoxEmitter(metadata))
                .EmitParse(request, new RecordingInstructionWriter()));

        Assert.Contains("metadata is unavailable", exception.Message);
    }

    [Fact]
    public void ParseUsesMemory64CharacterAddressing()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            target: WasmTarget.Wasm64);
        var request = Request(
            "InternalParse",
            RuntimeIntrinsic.EnumParse,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [metadata.EnumType],
            target: WasmTarget.Wasm64);
        var code = new RecordingInstructionWriter();

        new EnumParseEmitter(
                metadata,
                metadata,
                metadata.Layouts,
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                new EnumNumericParseEmitter(
                    metadata.Layouts,
                    new AddressInstructionEmitter(metadata.Layouts),
                    metadata.Layouts),
                metadata,
                CreateEnumValueBoxEmitter(metadata))
            .EmitParse(request, code);

        Assert.Contains(WasmOpcodes.I64ExtendI32Unsigned, code.ToArray());
        Assert.Contains(WasmOpcodes.I64Add, code.ToArray());
    }

    [Theory]
    [InlineData("InternalParse", 3)]
    [InlineData("InternalTryParse", 4)]
    public void TypeBasedParseDelegatesValidationParsingAndBoxing(
        string methodName,
        int parameterCount)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var parameterKinds = parameterCount == 3
            ? new[] { CliValueKind.ManagedReference, CliValueKind.ManagedReference, CliValueKind.I4 }
            : new[] { CliValueKind.ManagedReference, CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedAddress };
        var parameterTypes = parameterKinds
            .Select(CliTypeIdentity.FromStackKind)
            .ToImmutableArray();
        var request = Request(
            methodName,
            RuntimeIntrinsic.EnumParse,
            methodName == "InternalParse" ? CliValueKind.ManagedReference : CliValueKind.I4,
            parameterKinds,
            parameterKinds,
            parameterSignatureTypes: parameterTypes);
        var code = new RecordingInstructionWriter();

        CreateEnumParseEmitter(metadata).EmitParse(request, code);

        Assert.Contains(WasmOpcodes.Call, code.ToArray());
        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
    }

    [Fact]
    public void ParseReadsNamesFromCharacterArrays()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var characterArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("char", CliValueKind.I4));
        var request = Request(
            "InternalParse",
            RuntimeIntrinsic.EnumParse,
            CliValueKind.I4,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [metadata.EnumType],
            parameterSignatureTypes:
            [characterArray, CliTypeIdentity.Primitive("bool", CliValueKind.I4)]);
        var code = new RecordingInstructionWriter();

        CreateEnumParseEmitter(metadata).EmitParse(request, code);

        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
        Assert.Contains(WasmOpcodes.I32Load16Unsigned, code.ToArray());
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Equal)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Equal)]
    public void ToStringEmitsWidthAwareValueMatching(
        string underlyingName,
        CliValueKind stackKind,
        byte comparison)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        CreateToStringEmitter(metadata)
            .EmitToString(request, code);

        Assert.Contains(comparison, code.ToArray());
    }

    [Fact]
    public void ToStringSupportsFormatAndFlagsCandidates()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4),
            flags: true);
        var request = Request(
            "InternalFormat",
            RuntimeIntrinsic.EnumFormat,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        CreateToStringEmitter(metadata)
            .EmitToString(request, code);

        Assert.Contains(WasmOpcodes.I32Load16Unsigned, code.ToArray());
        Assert.Contains(WasmOpcodes.I32Or, code.ToArray());
    }

    [Theory]
    [InlineData("i1")]
    [InlineData("i2")]
    [InlineData("u2")]
    [InlineData("char")]
    [InlineData("u4")]
    [InlineData("u8")]
    public void ToStringCoversNarrowDecimalAndHexWidths(string underlyingName)
    {
        var stackKind = underlyingName == "u8"
            ? CliValueKind.I8
            : CliValueKind.I4;
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        CreateToStringEmitter(metadata)
            .EmitToString(request, code);

        Assert.Contains(WasmOpcodes.I32Equal, code.ToArray());
    }

    [Fact]
    public void ToStringEmitsMemory64FallbackAddressing()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i8", CliValueKind.I8),
            target: WasmTarget.Wasm64);
        var request = Request(
            "InternalToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
                CliValueKind.ManagedReference],
            target: WasmTarget.Wasm64);
        var code = new RecordingInstructionWriter();

        CreateToStringEmitter(metadata)
            .EmitToString(request, code);

        Assert.Contains(WasmOpcodes.I64Multiply, code.ToArray());
        Assert.Contains(WasmOpcodes.I64Add, code.ToArray());
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Equal)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Equal)]
    public void ToStringSupportsConstrainedEnumReceivers(
        string underlyingName,
        CliValueKind stackKind,
        byte equality)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "ToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [],
            [CliValueKind.ManagedAddress],
            constrainedType: metadata.EnumType);
        var code = new RecordingInstructionWriter();

        CreateToStringEmitter(metadata)
            .EmitToString(request, code);

        Assert.Contains(equality, code.ToArray());
    }

    [Fact]
    public void ToStringRequiresAClosedConstrainedReceiverType()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "ToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [],
            [CliValueKind.ManagedAddress]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateToStringEmitter(metadata)
                .EmitToString(request, new RecordingInstructionWriter()));

        Assert.Contains("closed receiver type", exception.Message);
    }

    [Fact]
    public void ToStringRejectsUnavailableConstrainedEnumMetadata()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var missing = CliTypeIdentity.Named(
            new AssemblyIdentity("Missing"),
            "Tests",
            "Missing",
            isValueType: true);
        var request = Request(
            "ToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [],
            [CliValueKind.ManagedAddress],
            constrainedType: missing);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateToStringEmitter(metadata)
                .EmitToString(request, new RecordingInstructionWriter()));

        Assert.Contains("metadata is unavailable", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToStringRecognizesPrimitiveAndNamedStringFormats(bool primitive)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var stringType = primitive
            ? CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference)
            : CliTypeIdentity.Named(
                TestAssembly,
                "System",
                "String",
                isValueType: false);
        var request = Request(
            "ToString",
            RuntimeIntrinsic.EnumToString,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            parameterSignatureTypes: [stringType]);
        var code = new RecordingInstructionWriter();

        CreateToStringEmitter(metadata)
            .EmitToString(request, code);

        Assert.Contains(WasmOpcodes.I32Load16Unsigned, code.ToArray());
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32Store)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64Store)]
    public void ToObjectNumericPathAllocatesAndStoresTheValue(
        string underlyingName,
        CliValueKind stackKind,
        byte store)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind));
        var request = Request(
            "InternalToObject",
            RuntimeIntrinsic.EnumToObject,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, stackKind],
            [CliValueKind.ManagedReference, stackKind]);
        var code = new RecordingInstructionWriter();

        new EnumToObjectEmitter(
                metadata,
                metadata,
                metadata,
                metadata.Layouts,
                metadata.Layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                metadata)
            .EmitToObject(request, code);

        Assert.Contains(WasmOpcodes.Call, code.ToArray());
        Assert.Contains(store, code.ToArray());
    }

    [Fact]
    public void ToObjectReferencePathRejectsNullAndInvalidObjectInputs()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalToObject",
            RuntimeIntrinsic.EnumToObject,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumToObjectEmitter(
                metadata,
                metadata,
                metadata,
                metadata.Layouts,
                metadata.Layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                metadata)
            .EmitToObject(request, code);

        Assert.Contains(WasmOpcodes.Throw, code.ToArray());
    }

    [Theory]
    [InlineData("System.Boolean")]
    [InlineData("System.Char")]
    [InlineData("System.SByte")]
    [InlineData("System.Byte")]
    [InlineData("System.Int16")]
    [InlineData("System.UInt16")]
    [InlineData("System.Int32")]
    [InlineData("System.UInt32")]
    [InlineData("System.Int64")]
    [InlineData("System.UInt64")]
    public void ToObjectAcceptsEveryReachableBoxedNumericType(string boxedType)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            boxedType: boxedType);
        var request = Request(
            "InternalToObject",
            RuntimeIntrinsic.EnumToObject,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumToObjectEmitter(
                metadata,
                metadata,
                metadata,
                metadata.Layouts,
                metadata.Layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                metadata)
            .EmitToObject(request, code);

        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Theory]
    [InlineData("System.Int32", "i8", WasmOpcodes.I64ExtendI32Signed)]
    [InlineData("System.UInt32", "u8", WasmOpcodes.I64ExtendI32Unsigned)]
    [InlineData("System.Int64", "i4", WasmOpcodes.I32WrapI64)]
    public void ToObjectConvertsBoxedNumericWidths(
        string boxedType,
        string underlyingName,
        byte conversion)
    {
        var stackKind = underlyingName is "i8" or "u8"
            ? CliValueKind.I8
            : CliValueKind.I4;
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive(underlyingName, stackKind),
            boxedType: boxedType);
        var request = Request(
            "InternalToObject",
            RuntimeIntrinsic.EnumToObject,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumToObjectEmitter(
                metadata,
                metadata,
                metadata,
                metadata.Layouts,
                metadata.Layouts,
                WasmRuntimeImports.CreateCatalog(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                new AddressInstructionEmitter(metadata.Layouts),
                metadata.Layouts,
                metadata)
            .EmitToObject(request, code);

        Assert.Contains(conversion, code.ToArray());
    }

    [Theory]
    [InlineData("i4", CliValueKind.I8, WasmOpcodes.I64ExtendI32Signed)]
    [InlineData("u4", CliValueKind.I8, WasmOpcodes.I64ExtendI32Unsigned)]
    public void ConvertUsesTheUnderlyingSignednessForWidening(
        string underlyingName,
        CliValueKind resultKind,
        byte conversion)
    {
        var underlying = CliTypeIdentity.Primitive(underlyingName, CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToInt64",
            RuntimeIntrinsic.EnumConvert,
            resultKind,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(conversion, code.ToArray());
    }

    [Fact]
    public void ConvertNormalizesExplicitIConvertibleMethodNames()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "System.IConvertible.ToInt32",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.I4,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, WasmOpcodes.I32EqualZero)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64EqualZero)]
    public void ConvertBooleanUsesTheUnderlyingWidth(
        string underlyingName,
        CliValueKind stackKind,
        byte comparison)
    {
        var underlying = CliTypeIdentity.Primitive(underlyingName, stackKind);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToBoolean",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.I4,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(comparison, code.ToArray());
        Assert.Contains(WasmOpcodes.I32EqualZero, code.ToArray());
    }

    [Fact]
    public void ConvertWideValuesToNarrowValuesWrapTheStorage()
    {
        var underlying = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToInt32",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.I4,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(WasmOpcodes.I32WrapI64, code.ToArray());
    }

    [Theory]
    [InlineData("i4", "InternalToSingle", CliValueKind.F4, WasmOpcodes.F32ConvertI32Signed)]
    [InlineData("u4", "InternalToSingle", CliValueKind.F4, WasmOpcodes.F32ConvertI32Unsigned)]
    [InlineData("i8", "InternalToSingle", CliValueKind.F4, WasmOpcodes.F32ConvertI64Signed)]
    [InlineData("u8", "InternalToSingle", CliValueKind.F4, WasmOpcodes.F32ConvertI64Unsigned)]
    [InlineData("i4", "InternalToDouble", CliValueKind.F8, WasmOpcodes.F64ConvertI32Signed)]
    [InlineData("u4", "InternalToDouble", CliValueKind.F8, WasmOpcodes.F64ConvertI32Unsigned)]
    [InlineData("i8", "InternalToDouble", CliValueKind.F8, WasmOpcodes.F64ConvertI64Signed)]
    [InlineData("u8", "InternalToDouble", CliValueKind.F8, WasmOpcodes.F64ConvertI64Unsigned)]
    public void ConvertFloatingValuesUsesWidthAndSignedness(
        string underlyingName,
        string methodName,
        CliValueKind resultKind,
        byte conversion)
    {
        var stackKind = underlyingName is "i8" or "u8"
            ? CliValueKind.I8
            : CliValueKind.I4;
        var underlying = CliTypeIdentity.Primitive(underlyingName, stackKind);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            methodName,
            RuntimeIntrinsic.EnumConvert,
            resultKind,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(conversion, code.ToArray());
    }

    [Fact]
    public void ConvertDecimalWritesTheValueLayout()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToDecimal",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ValueType,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(WasmOpcodes.I32Store, code.ToArray());
    }

    [Fact]
    public void ConvertDecimalUsesThePlannedTemporaryOffset()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToDecimal",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ValueType,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference],
            valueOffset: 8);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(WasmOpcodes.I32Add, code.ToArray());
    }

    [Theory]
    [InlineData("u4", CliValueKind.I4, WasmOpcodes.I32Store)]
    [InlineData("i8", CliValueKind.I8, WasmOpcodes.I64ShiftRightSigned)]
    [InlineData("u8", CliValueKind.I8, WasmOpcodes.I64ShiftRightUnsigned)]
    public void ConvertDecimalHandlesEveryIntegralWidthAndSignedness(
        string underlyingName,
        CliValueKind stackKind,
        byte expectedOperation)
    {
        var underlying = CliTypeIdentity.Primitive(underlyingName, stackKind);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToDecimal",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ValueType,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(expectedOperation, code.ToArray());
    }

    [Fact]
    public void ConvertDecimalRejectsNonIntegralEnumStorage()
    {
        var underlying = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
        var metadata = new EnumMetadataFixture(underlying);
        var request = Request(
            "InternalToDecimal",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ValueType,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumConvertEmitter(
                    new FixedStorageResolver(metadata, underlying),
                    new RecordingStringEmitter(),
                    new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                    metadata.Layouts,
                    metadata)
                .EmitConvert(request, new RecordingInstructionWriter()));

        Assert.Contains("does not support", exception.Message);
    }

    [Fact]
    public void ConvertDateTimeAlwaysEmitsInvalidCast()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var request = Request(
            "InternalToDateTime",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ValueType,
            [CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, metadata.Underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        Assert.Contains(WasmOpcodes.Throw, code.ToArray());
    }

    [Fact]
    public void ConvertToTypeDelegatesToStringCapability()
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var strings = new RecordingStringEmitter();
        var request = Request(
            "InternalToType",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference]);

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, metadata.Underlying),
                strings,
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, new RecordingInstructionWriter());

        Assert.True(strings.Called);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64EqualZero)]
    public void ConvertToTypeValidatesTheRequestedTypeAndNull(
        WasmTarget target,
        byte nullComparison)
    {
        var metadata = new EnumMetadataFixture(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            target: target);
        var request = Request(
            "InternalToType",
            RuntimeIntrinsic.EnumConvert,
            CliValueKind.ManagedReference,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            target: target);
        var code = new RecordingInstructionWriter();

        new EnumConvertEmitter(
                new FixedStorageResolver(metadata, metadata.Underlying),
                new RecordingStringEmitter(),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts,
                metadata)
            .EmitConvert(request, code);

        var bytes = code.ToArray();
        Assert.Contains(nullComparison, bytes);
        Assert.Contains(WasmOpcodes.I32Load, bytes);
        Assert.Contains(WasmOpcodes.I32Equal, bytes);
        Assert.Contains(WasmOpcodes.Throw, bytes);
    }

    [Theory]
    [InlineData("i1")]
    [InlineData("u1")]
    [InlineData("i2")]
    [InlineData("u2")]
    [InlineData("i4")]
    [InlineData("u4")]
    [InlineData("i8")]
    [InlineData("u8")]
    [InlineData("char")]
    public void TypeCodeMapsEverySupportedUnderlyingType(string underlyingName)
    {
        var stackKind = underlyingName is "i8" or "u8"
            ? CliValueKind.I8
            : CliValueKind.I4;
        var underlying = CliTypeIdentity.Primitive(underlyingName, stackKind);
        var metadata = new EnumMetadataFixture(underlying);
        var code = new RecordingInstructionWriter();

        new EnumTypeCodeEmitter(
                new FixedStorageResolver(metadata, underlying),
                new NoOpNullCheckEmitter())
            .EmitTypeCode(
                code,
                0,
                1,
                2,
                metadata.EnumType,
                CliValueKind.ManagedAddress);

        Assert.Contains(WasmOpcodes.I32Constant, code.ToArray());
        Assert.Contains(WasmOpcodes.LocalSet, code.ToArray());
    }

    [Fact]
    public void TypeCodeRejectsAnUnsupportedUnderlyingType()
    {
        var underlying = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
        var metadata = new EnumMetadataFixture(underlying);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumTypeCodeEmitter(
                    new FixedStorageResolver(metadata, underlying),
                    new NoOpNullCheckEmitter())
                .EmitTypeCode(
                    new RecordingInstructionWriter(),
                    0,
                    1,
                    2,
                    metadata.EnumType,
                    CliValueKind.ManagedAddress));

        Assert.Contains("does not support", exception.Message);
    }

    [Fact]
    public void TypeCodeRejectsAnEmptyReachableStorageSet()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumTypeCodeEmitter(
                    new EmptyStorageResolver(),
                    new NoOpNullCheckEmitter())
                .EmitTypeCode(
                    new RecordingInstructionWriter(),
                    0,
                    1,
                    2));

        Assert.Contains("reachable enum descriptor", exception.Message);
    }

    [Fact]
    public void TypeCodeRejectsAnUnknownConstrainedEnum()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var unknown = CliTypeIdentity.Named(
            new AssemblyIdentity("other-type-code-assembly"),
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumTypeCodeEmitter(
                    new FixedStorageResolver(metadata, underlying),
                    new NoOpNullCheckEmitter())
                .EmitTypeCode(
                    new RecordingInstructionWriter(),
                    0,
                    1,
                    2,
                    unknown,
                    CliValueKind.ManagedAddress));

        Assert.Contains("no descriptor", exception.Message);
    }

    [Fact]
    public void TypeCodeRequiresAClosedConstrainedReceiverType()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EnumTypeCodeEmitter(
                    new FixedStorageResolver(metadata, underlying),
                    new NoOpNullCheckEmitter())
                .EmitTypeCode(
                    new RecordingInstructionWriter(),
                    0,
                    1,
                    2,
                    receiverKind: CliValueKind.ManagedAddress));

        Assert.Contains("closed receiver type", exception.Message);
    }

    [Fact]
    public void HashCodeUsesTheClosedReceiverLayoutWhenStorageIsUnavailable()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var code = new RecordingInstructionWriter();

        new EnumHashCodeEmitter(
                new EmptyStorageResolver(),
                new NoOpNullCheckEmitter(),
                metadata.Layouts,
                metadata.Layouts)
            .EmitHashCode(
                code,
                CliValueKind.ManagedAddress,
                metadata.EnumType,
                0,
                1,
                2,
                3);

        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
    }

    [Theory]
    [InlineData("i4", WasmOpcodes.I32And, WasmOpcodes.I32Equal)]
    [InlineData("i8", WasmOpcodes.I64And, WasmOpcodes.I64Equal)]
    public void HasFlagUsesTheStorageWidth(
        string underlyingName,
        byte bitwise,
        byte equality)
    {
        var underlying = CliTypeIdentity.Primitive(
            underlyingName,
            underlyingName == "i8" ? CliValueKind.I8 : CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var emitter = new EnumHasFlagEmitter(
            new FixedStorageResolver(metadata, underlying),
            new NoOpNullCheckEmitter(),
            new EnumValuePairEmitter(metadata.Layouts),
            new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
            metadata.Layouts);
        var code = new RecordingInstructionWriter();

        emitter.EmitHasFlag(code, 0, 1, 2, 3);

        Assert.Contains(bitwise, code.ToArray());
        Assert.Contains(equality, code.ToArray());
    }

    [Theory]
    [InlineData("i4", WasmOpcodes.I32And, WasmOpcodes.I32Equal)]
    [InlineData("i8", WasmOpcodes.I64And, WasmOpcodes.I64Equal)]
    public void HasFlagSupportsAConstrainedEnumReceiver(
        string underlyingName,
        byte bitwise,
        byte equality)
    {
        var underlying = CliTypeIdentity.Primitive(
            underlyingName,
            underlyingName == "i8" ? CliValueKind.I8 : CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var emitter = new EnumHasFlagEmitter(
            new FixedStorageResolver(metadata, underlying),
            new NoOpNullCheckEmitter(),
            new EnumValuePairEmitter(metadata.Layouts),
            new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
            metadata.Layouts);
        var code = new RecordingInstructionWriter();

        emitter.EmitHasFlag(
            code,
            0,
            1,
            2,
            3,
            CliValueKind.ManagedAddress,
            metadata.EnumType);

        Assert.Contains(bitwise, code.ToArray());
        Assert.Contains(equality, code.ToArray());
    }

    [Fact]
    public void HasFlagRequiresAClosedConstrainedReceiverType()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var emitter = new EnumHasFlagEmitter(
            new FixedStorageResolver(metadata, underlying),
            new NoOpNullCheckEmitter(),
            new EnumValuePairEmitter(metadata.Layouts),
            new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
            metadata.Layouts);

        var exception = Assert.Throws<InvalidOperationException>(() => emitter.EmitHasFlag(
            new RecordingInstructionWriter(),
            0,
            1,
            2,
            3,
            CliValueKind.ManagedAddress));

        Assert.Contains("closed receiver type", exception.Message);
    }

    [Fact]
    public void HasFlagRejectsAnUnreachableConstrainedReceiverType()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var metadata = new EnumMetadataFixture(underlying);
        var unknown = CliTypeIdentity.Named(
            new AssemblyIdentity("other-has-flag-assembly"),
            "Tests",
            "Missing",
            true,
            CliValueKind.I4);
        var emitter = new EnumHasFlagEmitter(
            new FixedStorageResolver(metadata, underlying),
            new NoOpNullCheckEmitter(),
            new EnumValuePairEmitter(metadata.Layouts),
            new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
            metadata.Layouts);

        var exception = Assert.Throws<InvalidOperationException>(() => emitter.EmitHasFlag(
            new RecordingInstructionWriter(),
            0,
            1,
            2,
            3,
            CliValueKind.ManagedAddress,
            unknown));

        Assert.Contains("no reachable enum descriptor", exception.Message);
    }

    private static EnumGetNameEmitter CreateGetNameEmitter(
        EnumMetadataFixture metadata) => new(
            metadata,
            metadata,
            metadata.Layouts,
            metadata.Layouts,
            new ReferenceComparisonEmitter(metadata.Layouts),
            new AddressInstructionEmitter(metadata.Layouts),
            CreateTypeArgumentValidator(metadata));

    private static EnumGetNamesEmitter CreateGetNamesEmitter(
        EnumMetadataFixture metadata) => new(
            metadata,
            metadata,
            metadata.Layouts,
            WasmRuntimeImports.CreateCatalog(),
            metadata.Layouts,
            new AddressInstructionEmitter(metadata.Layouts),
            CreateTypeArgumentValidator(metadata));

    private static EnumGetValuesEmitter CreateGetValuesEmitter(
        EnumMetadataFixture metadata) => new(
            metadata,
            metadata,
            metadata.Layouts,
            metadata.Layouts,
            WasmRuntimeImports.CreateCatalog(),
            metadata.Layouts,
            new AddressInstructionEmitter(metadata.Layouts),
            CreateTypeArgumentValidator(metadata));

    private static EnumGetUnderlyingTypeEmitter CreateGetUnderlyingTypeEmitter(
        EnumMetadataFixture metadata) => new(
            metadata,
            metadata,
            metadata.Layouts,
            WasmRuntimeImports.CreateCatalog(),
            new AddressInstructionEmitter(metadata.Layouts),
            CreateTypeArgumentValidator(metadata));

    private static EnumToStringEmitter CreateToStringEmitter(
        EnumMetadataFixture metadata) => new(
            metadata,
            metadata,
            metadata.Layouts,
            metadata.Layouts,
            new AddressInstructionEmitter(metadata.Layouts),
            new EnumValueFormatter(
                metadata,
                metadata,
                metadata,
                metadata.Layouts,
                new AddressInstructionEmitter(metadata.Layouts),
                new EnumNumericFormatter(
                    metadata,
                    metadata,
                    metadata,
                    metadata.Layouts,
                    WasmRuntimeImports.CreateCatalog())),
            CreateTypeArgumentValidator(metadata));

    private static EnumTypeArgumentValidator CreateTypeArgumentValidator(
        EnumMetadataFixture metadata) => new(
            metadata,
            new TypeObjectIdReader(
                new ReferenceComparisonEmitter(metadata.Layouts),
                new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
                metadata.Layouts),
            new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0));

    private static EnumValueBoxEmitter CreateEnumValueBoxEmitter(
        EnumMetadataFixture metadata) => new EnumValueBoxEmitter(
        metadata.Layouts,
        metadata.Layouts,
        WasmRuntimeImports.CreateCatalog(),
        new AddressInstructionEmitter(metadata.Layouts),
        metadata.Layouts);

    private static EnumParseEmitter CreateEnumParseEmitter(
        EnumMetadataFixture metadata) => new(
        metadata,
        metadata,
        metadata.Layouts,
        new ImplicitExceptionEmitter(metadata.Layouts, metadata.Layouts, 0),
        new AddressInstructionEmitter(metadata.Layouts),
        metadata.Layouts,
        new EnumNumericParseEmitter(
            metadata.Layouts,
            new AddressInstructionEmitter(metadata.Layouts),
            metadata.Layouts),
        metadata,
        CreateEnumValueBoxEmitter(metadata));

    private static RuntimeIntrinsicEmissionRequest Request(
        string name,
        RuntimeIntrinsic intrinsic,
        CliValueKind returnType,
        CliValueKind[] parameters,
        CliValueKind[] stack,
        ImmutableArray<CliTypeIdentity> methodArguments = default,
        WasmTarget target = WasmTarget.Wasm32,
        CliTypeIdentity? constrainedType = null,
        ImmutableArray<CliTypeIdentity> parameterSignatureTypes = default,
        int valueOffset = 0)
    {
        var context = returnType == CliValueKind.ValueType
            ? CreateMethodEmissionContext(Math.Max(8, stack.Length)) with
            {
                ValueLayout = new ValueFrameLayout(
                    16,
                    [],
                    [],
                    ImmutableDictionary<int, int>.Empty.Add(0, valueOffset),
                    [])
            }
            : null;
        var instruction = EmitterTestSupport.CreateInstructionRequest(
            CilOperation.Call,
            stack,
            context: context,
            maxStack: Math.Max(8, stack.Length));
        var signature = parameterSignatureTypes.IsDefault
            ? MethodSignatureModel.Create(returnType, parameters)
            : new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(returnType),
                parameterSignatureTypes);
        var definition = new MethodDefinitionModel(
            new EntityKey(TestAssembly, NextToken()),
            EnumDeclaringType,
            name,
            true,
            signature,
            0);
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(TestAssembly, "System", "Enum", false),
            methodArguments.IsDefault ? [] : methodArguments,
            signature);
        return new(
            new CallEmissionRequest(instruction, instance, 0, stack.Length),
            intrinsic,
            constrainedType,
            WasmTargetLayout.For(target),
            EmitterTestSupport.CreateFunctionIndexResolver(new FakeProgram()));
    }

    private static int _nextToken = 0x06000001;
    private static int NextToken() => _nextToken++;

    private static readonly AssemblyIdentity TestAssembly = new("EnumEmitterTests");
    private static readonly EntityKey EnumDeclaringType = new(TestAssembly, 0x02000001);
    private static readonly EntityKey EnumTypeKey = new(TestAssembly, 0x02000002);
    private static readonly EntityKey BoxedTypeKey = new(TestAssembly, 0x02000003);

    private sealed class EnumMetadataFixture :
        IEnumMetadataSource,
        ITypeDescriptorSource,
        ITypeRepository,
        IValueLayoutProvider,
        ITypeLayoutProvider,
        IStaticDataLayout,
        IRuntimeObjectLayout,
        IManagedExceptionObjectProvider,
        IEnumTypeArgumentValidator
    {
        private readonly CliTypeIdentity _underlying;
        private readonly bool _flags;
        private readonly string? _boxedType;

        public EnumMetadataFixture(
            CliTypeIdentity underlying,
            bool flags = false,
            bool fallbackArrayLayout = false,
            WasmTarget target = WasmTarget.Wasm32,
            string? boxedType = null)
        {
            _underlying = underlying;
            _flags = flags;
            _boxedType = boxedType;
            Layouts = new TestLayouts(fallbackArrayLayout, target);
        }

        public CliTypeIdentity Underlying => _underlying;
        public CliTypeIdentity EnumType => CliTypeIdentity.Named(
            TestAssembly,
            "Tests",
            "State",
            true,
            _underlying.StackKind);
        public TestLayouts Layouts { get; }

        public ImmutableArray<EnumMetadataLayout> EnumMetadata =>
        [
            new(
                EnumTypeKey,
                7,
                400,
                _underlying,
                _flags,
                [
                    new("Zero", 0, new StringLayout(100, 4, 48)),
                    new("One", 1, new StringLayout(104, 3, 48)),
                    new("Two", 2, new StringLayout(108, 3, 48)),
                ]),
        ];

        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => _boxedType is null
            ? [new(EnumTypeKey, 7, 0, 32, 0, 0, null)]
            : [
                new(EnumTypeKey, 7, 0, 32, 0, 0, null),
                new(BoxedTypeKey, 19, 0, 32, 0, 0, null),
            ];

        public void Validate(
            IWasmInstructionWriter code,
            int typeLocal,
            int typeIdLocal)
        {
        }
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];

        public TypeDefinitionModel GetTypeDefinition(EntityKey key)
        {
            if (key == BoxedTypeKey && _boxedType is not null)
            {
                return new(
                    BoxedTypeKey,
                    "System",
                    _boxedType["System.".Length..],
                    true,
                    [],
                    []);
            }
            return new(
                EnumTypeKey,
                "Tests",
                "State",
                true,
                [],
                [])
            {
                IsEnum = true,
                EnumUnderlyingType = _underlying,
                IsFlagsEnum = _flags,
            };
        }

        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            Layouts.GetValueLayout(type);

        public ObjectLayout GetObjectLayout(CliTypeIdentity type) =>
            Layouts.GetObjectLayout(type);

        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout) =>
            Layouts.GetObjectLayout(type, out layout);

        public ObjectLayout GetObjectLayout(EntityKey type) =>
            Layouts.GetObjectLayout(type);

        public int ReferenceArrayTypeId => Layouts.ReferenceArrayTypeId;
        public int StringTypeId => Layouts.StringTypeId;
        public int TypeTypeId => Layouts.TypeTypeId;
        public StringLayout GetStringLayout(string value) => Layouts.GetStringLayout(value);
        public int StaticDataEnd => Layouts.StaticDataEnd;
        public ImmutableArray<int> StaticRootAddresses => [];
        public ImmutableArray<DataSegment> DataSegments => [];
        public int StringLengthOffset => Layouts.StringLengthOffset;
        public int StringDataOffset => Layouts.StringDataOffset;
        public int ArrayLengthOffset => Layouts.ArrayLengthOffset;
        public int ArrayDataPointerOffset => Layouts.ArrayDataPointerOffset;
        public int ArrayElementTypeIdOffset => Layouts.ArrayElementTypeIdOffset;
        public int DelegateTargetOffset => Layouts.DelegateTargetOffset;
        public int DelegateMethodIdOffset => Layouts.DelegateMethodIdOffset;
        public int DelegateLeftOffset => Layouts.DelegateLeftOffset;
        public int DelegateRightOffset => Layouts.DelegateRightOffset;
        public int GetExceptionObject(ManagedExceptionKind kind) => 300 + (int)kind * 4;
    }

    private sealed class TestLayouts :
        ITargetLayout,
        IValueLayoutProvider,
        ITypeLayoutProvider,
        IStaticDataLayout,
        IRuntimeObjectLayout,
        IManagedExceptionObjectProvider
    {
        private readonly bool _fallbackArrayLayout;
        private readonly WasmTargetLayout _target;

        public TestLayouts(
            bool fallbackArrayLayout = false,
            WasmTarget target = WasmTarget.Wasm32)
        {
            _fallbackArrayLayout = fallbackArrayLayout;
            _target = WasmTargetLayout.For(target);
        }

        public WasmTargetLayout Target => _target;
        public int ReferenceArrayTypeId => 12;
        public int StringTypeId => 13;
        public int TypeTypeId => 14;
        public int StringLengthOffset => 4;
        public int StringDataOffset => 8;
        public int ArrayLengthOffset => 4;
        public int ArrayDataPointerOffset => 8;
        public int ArrayElementTypeIdOffset => 12;
        public int DelegateTargetOffset => 4;
        public int DelegateMethodIdOffset => 8;
        public int DelegateLeftOffset => 12;
        public int DelegateRightOffset => 16;
        public int StaticDataEnd => 1024;

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            var size = type.CanonicalName switch
            {
                "primitive:i1" or "primitive:u1" => 1,
                "primitive:i2" or "primitive:u2" => 2,
                "primitive:i8" or "primitive:u8" => 8,
                _ => 4,
            };
            return new(type, size, Math.Min(size, 4), []);
        }

        public ObjectLayout GetObjectLayout(CliTypeIdentity type)
        {
            if (_fallbackArrayLayout && type.Shape == CliTypeShape.SzArray)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.RuntimeContract,
                    "closed array layout is not reachable"));
            }
            return new(type.Shape is CliTypeShape.SzArray ? 40 : 7, 32, []);
        }

        public ObjectLayout GetObjectLayout(EntityKey type) => new(7, 32, []);

        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            try
            {
                layout = GetObjectLayout(type);
                return true;
            }
            catch (CompilerException)
            {
                layout = default;
                return false;
            }
        }

        public StringLayout GetStringLayout(string value) => new(200, value.Length, 8);
        public ImmutableArray<int> StaticRootAddresses => [];
        public ImmutableArray<DataSegment> DataSegments => [];
        public int GetExceptionObject(ManagedExceptionKind kind) => 300 + (int)kind * 4;
    }

    private sealed class FixedStorageResolver(
        EnumMetadataFixture metadata,
        CliTypeIdentity underlying) : IEnumStorageResolver
    {
        public ImmutableArray<EnumStorage> Resolve() =>
        [
            new(
                new TypeDescriptorLayout(EnumTypeKey, 7, 0, 32, 0, 0, null),
                metadata.EnumType,
                underlying,
                metadata.Layouts.GetValueLayout(underlying),
                4),
        ];
    }

    private sealed class EmptyStorageResolver : IEnumStorageResolver
    {
        public ImmutableArray<EnumStorage> Resolve() => [];
    }

    private sealed class NoOpNullCheckEmitter : IEnumNullCheckEmitter
    {
        public void Emit(IWasmInstructionWriter code, int objectLocal)
        {
        }
    }

    private sealed class RecordingStringEmitter : IEnumToStringEmitter
    {
        public bool Called { get; private set; }

        public void EmitToString(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
            Called = true;
    }
}
