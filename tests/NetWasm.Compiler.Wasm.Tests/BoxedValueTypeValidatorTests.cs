// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;
using NetWasm.Compiler.Wasm.Emission.Planning;

public sealed class BoxedValueTypeValidatorTests
{
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Byte =
        CliTypeIdentity.Primitive("u1", CliValueKind.I4);
    private static readonly CliTypeIdentity FirstEnum = EnumType("First");
    private static readonly CliTypeIdentity SecondEnum = EnumType("Second");
    private static readonly CliTypeIdentity ByteEnum = EnumType("ByteBacked");

    [Fact]
    public void ValidateAcceptsOnlyTheExactBoxedTypeForNonEnumValues()
    {
        var instructions = Validate(Int32, []);

        Assert.Equal([21], AcceptedTypeIds(instructions));
        Assert.Single(instructions.Where(instruction =>
            instruction.Opcode == WasmOpcodes.If));
        Assert.True(ContainsManagedThrow(instructions));
    }

    [Fact]
    public void ValidateAcceptsEveryEnumWithThePrimitiveTargetsStorage()
    {
        var instructions = Validate(Int32, Storages());

        Assert.Equal([11, 12, 21], AcceptedTypeIds(instructions));
        Assert.DoesNotContain(13, AcceptedTypeIds(instructions));
    }

    [Fact]
    public void ValidateAcceptsSameStorageEnumsAndTheirPrimitiveForEnumTargets()
    {
        var instructions = Validate(FirstEnum, Storages());

        Assert.Equal([11, 12, 21], AcceptedTypeIds(instructions));
    }

    [Fact]
    public void ValidateOmitsThePrimitiveAliasWhenItsBoxedLayoutIsUnreachable()
    {
        var code = Validate(FirstEnum, Storages(), primitiveLayoutReachable: false);

        Assert.Equal([11, 12], AcceptedTypeIds(code));
    }

    private static ImmutableArray<WasmInstruction> Validate(
        CliTypeIdentity targetType,
        ImmutableArray<EnumStorage> storages,
        bool primitiveLayoutReachable = true)
    {
        var layouts = new TypeLayouts(primitiveLayoutReachable);
        var exceptionLayouts = new RecordingLayoutProvider();
        var validator = new BoxedValueTypeValidator(
            layouts,
            new StorageResolver(storages),
            new ImplicitExceptionEmitter(exceptionLayouts), new KeyedDispatchEmitter());
        var writer = new RecordingInstructionWriter();

        ((IBoxedValueTypeValidator)validator).Validate(writer, 3, targetType, OptionalFunctionIndex.At(7));

        return writer.ToInstructions();
    }

    // The type ids the emitted membership accepts: an equality its constant, a range test (the id less the run's first,
    // below the run's length) each id of the run.
    private static int[] AcceptedTypeIds(ImmutableArray<WasmInstruction> instructions)
    {
        var accepted = new List<int>();
        for (var index = 0; index + 1 < instructions.Length; index++)
        {
            if (instructions[index].Opcode != WasmOpcodes.I32Constant)
            {
                continue;
            }
            var value = instructions[index].Operand.SignedValue;
            if (instructions[index + 1].Opcode == WasmOpcodes.I32Equal)
            {
                accepted.Add(value);
            }
            else if (instructions[index + 1].Opcode == WasmOpcodes.I32Subtract &&
                instructions[index + 2].Opcode == WasmOpcodes.I32Constant &&
                instructions[index + 3].Opcode == WasmOpcodes.I32LessThanUnsigned)
            {
                accepted.AddRange(Enumerable.Range(value, instructions[index + 2].Operand.SignedValue));
            }
        }
        return [.. accepted];
    }

    private static ImmutableArray<EnumStorage> Storages() =>
        [
            Storage(FirstEnum, Int32, 11),
            Storage(SecondEnum, Int32, 12),
            Storage(ByteEnum, Byte, 13),
        ];

    private static EnumStorage Storage(
        CliTypeIdentity enumType,
        CliTypeIdentity underlyingType,
        int typeId) => new(
            typeId,
            enumType,
            underlyingType,
            new ValueLayout(underlyingType, 4, 4, []),
            4);

    private static CliTypeIdentity EnumType(string name) =>
        CliTypeIdentity.Named(Assembly, "Test", name, isValueType: true);

    private sealed class StorageResolver(
        ImmutableArray<EnumStorage> storages) : IEnumStorageResolver
    {
        public ImmutableArray<EnumStorage> Resolve() => storages;
    }

    private sealed class TypeLayouts(bool primitiveLayoutReachable) : ITypeLayoutProvider
    {
        public int StringTypeId => 31;
        public int TypeTypeId => 32;

        public ObjectLayout GetObjectLayout(CliTypeIdentity type) =>
            new(type.Equals(FirstEnum) ? 11 :
                type.Equals(SecondEnum) ? 12 :
                type.Equals(ByteEnum) ? 13 :
                type.Equals(Int32) ? 21 :
                type.Equals(Byte) ? 22 :
                throw new InvalidOperationException($"Unexpected type {type}."),
                16,
                []);

        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            if (!primitiveLayoutReachable && (type.Equals(Int32) || type.Equals(Byte)))
            {
                layout = default;
                return false;
            }

            try
            {
                layout = GetObjectLayout(type);
                return true;
            }
            catch (InvalidOperationException)
            {
                layout = default;
                return false;
            }
        }

        public ObjectLayout GetObjectLayout(EntityKey type) =>
            throw new NotSupportedException();
    }
}
