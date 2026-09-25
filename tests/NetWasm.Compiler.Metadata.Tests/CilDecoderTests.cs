using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CilDecoderTests
{
    private static readonly string[] PrimitiveArrayMethods =
    [
        "SignedByte", "Byte", "Short", "UShort", "Int", "UInt",
        "Long", "Native", "Single", "Double", "Value",
        "LongConstant", "SingleConstant", "DoubleConstant",
    ];
    private static readonly string[] EnumFieldMethods =
        ["GetInstance", "SetInstance", "GetStatic", "SetStatic"];

    [Fact]
    public void DecoderRetainsResolvedStackKindsForExternalEnumFields()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "EnumField.Library",
            """
            namespace EnumField.Library;

            public enum Small : byte { Zero, One }
            public enum Wide : long { Zero, One }
            """);
        var application = assets.CompileSource(
            "EnumField.Application",
            """
            using EnumField.Library;

            namespace EnumField.Application;

            public sealed class Holder
            {
                public Small Instance;
                public static Wide Static;

                public Small GetInstance() => Instance;
                public void SetInstance(Small value) => Instance = value;
                public static Wide GetStatic() => Static;
                public static void SetStatic(Wide value) => Static = value;
            }
            """,
            library);
        using var lease = MetadataCompilationTestFactory.Load(
            application,
            [library, assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);

        var fieldInstructions = EnumFieldMethods
            .Select(name => methods.FindMethod(
                snapshot.EntryAssemblyIdentity,
                "EnumField.Application.Holder",
                name))
            .Select(method => bodies.ReadMethodBody(method).Instructions.Single(
                instruction => instruction.Operation is
                    CilOperation.LoadField or CilOperation.StoreField or
                    CilOperation.LoadStaticField or CilOperation.StoreStaticField))
            .ToArray();

        Assert.All(fieldInstructions, instruction =>
            Assert.IsType<CilOperand.FieldInstance>(instruction.Operand));
        Assert.Equal(
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.I8, CliValueKind.I8],
            fieldInstructions
                .Select(instruction =>
                    ((CilOperand.FieldInstance)instruction.Operand).Value.FieldType.StackKind)
                .ToArray());
    }

    [Fact]
    public void DecoderRetainsVolatilePrefixesFromOrdinaryRoslynFields()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "VolatileCil",
            """
            namespace VolatileCil;

            public static class EntryPoint
            {
                private static volatile int _value;

                public static int Run(int input)
                {
                    _value = input;
                    return _value;
                }
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var method = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "VolatileCil.EntryPoint",
            "Run");
        var instructions = bodies.ReadMethodBody(method).Instructions;

        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Operation == CilOperation.Volatile));
        Assert.Contains(instructions, instruction =>
            instruction.Operation == CilOperation.StoreStaticField);
        Assert.Contains(instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticField);

    }

    [Fact]
    public void DecoderRetainsCoreLibNumericOperationsAndConversionMetadata()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NumericCil",
            """
            namespace NumericCil;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var bits = ((value & 31) | 2) ^ 1;
                    bits = (bits << 3) >> 2;
                    bits += ~value + -value;
                    bits += (int)((uint)value / 3U);
                    bits += (int)((uint)value % 5U);
                    bits += value / 7 + value % 11;
                    bits += checked((byte)value);
                    return bits;
                }

                public static long ConvertSigned(long value)
                {
                    var result = (long)((sbyte)value + (byte)value + (short)value + (ushort)value);
                    result += checked((sbyte)value) + checked((byte)value);
                    result += checked((short)value) + checked((ushort)value);
                    result += checked((int)value) + checked((uint)value);
                    result += checked((long)value) + (long)checked((ulong)value);
                    return result;
                }

                public static long ConvertUnsigned(ulong value)
                {
                    var result = (long)(checked((sbyte)value) + checked((byte)value));
                    result += checked((short)value) + checked((ushort)value);
                    result += checked((int)value) + checked((uint)value);
                    result += checked((long)value) + (long)checked((ulong)value);
                    return result;
                }

                public static nint ConvertNative(long value) => checked((nint)value);
                public static nuint ConvertNativeUnsigned(long value) => checked((nuint)value);
                public static nint ConvertUnsignedToNative(ulong value) => checked((nint)value);
                public static nuint ConvertUnsignedToNativeUnsigned(ulong value) => checked((nuint)value);
                public static long ConvertFloat(double value) => checked((long)value);
                public static double ConvertUnsignedFloat(uint value) => value;
                public static double ConvertUnsignedFloat64(ulong value) => value;
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var methodNames = System.Collections.Immutable.ImmutableArray.Create(
            "Run", "ConvertSigned", "ConvertUnsigned", "ConvertNative",
            "ConvertNativeUnsigned", "ConvertUnsignedToNative",
            "ConvertUnsignedToNativeUnsigned", "ConvertFloat",
            "ConvertUnsignedFloat", "ConvertUnsignedFloat64");
        var instructions = methodNames
            .Select(name => methods.FindMethod(
                snapshot.EntryAssemblyIdentity, "NumericCil.EntryPoint", name))
            .SelectMany(method => bodies.ReadMethodBody(method).Instructions)
            .ToArray();
        var operations = instructions.Select(instruction => instruction.Operation).ToHashSet();

        Assert.Contains(CilOperation.BitwiseAnd, operations);
        Assert.Contains(CilOperation.BitwiseOr, operations);
        Assert.Contains(CilOperation.BitwiseXor, operations);
        Assert.Contains(CilOperation.ShiftLeft, operations);
        Assert.Contains(CilOperation.ShiftRightSigned, operations);
        Assert.Contains(CilOperation.Negate, operations);
        Assert.Contains(CilOperation.OnesComplement, operations);
        Assert.Contains(CilOperation.Divide, operations);
        Assert.Contains(CilOperation.DivideUnsigned, operations);
        Assert.Contains(CilOperation.Remainder, operations);
        Assert.Contains(CilOperation.RemainderUnsigned, operations);
        Assert.Contains(CilOperation.ConvertFloatUnsigned, operations);
        Assert.Contains(instructions, instruction =>
            instruction.Operation == CilOperation.ConvertNumeric &&
            instruction.Operand is CilOperand.NumericConversion
            {
                BitWidth: 8,
                DestinationUnsigned: true,
                Checked: true,
            });
        Assert.Contains(instructions, instruction =>
            instruction.Operand is CilOperand.NumericConversion
            {
                BitWidth: 64,
                DestinationUnsigned: false,
                Checked: true,
                SourceUnsigned: true,
            });

    }

    [Fact]
    public void DecoderRetainsFoundationalValueTypeAndConstrainedOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueCil",
            """
            namespace ValueCil;

            public interface IRead
            {
                int Read();
            }

            public struct Pair : IRead
            {
                public int Left;
                public int Right;

                public Pair(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Read() => Left + Right;
            }

            public static class EntryPoint
            {
                public static Pair Build(int input)
                {
                    Pair value = new Pair(input, input + 1);
                    return value;
                }

                public static int ReadDefault()
                {
                    Pair value = default;
                    return value.Read();
                }

                public static object Box(Pair value) => value;
                public static Pair Unbox(object value) => (Pair)value;

                public static int Read<T>(T value) where T : IRead => value.Read();
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var type = "ValueCil.EntryPoint";
        string[] methodNames = ["Build", "ReadDefault", "Box", "Unbox", "Read"];
        CilOperation[] operations = [
            .. methodNames
                    .Select(name => methods.FindMethod(
                        snapshot.EntryAssemblyIdentity, type, name))
                    .SelectMany(method => bodies.ReadMethodBody(method).Instructions)
                    .Select(instruction => instruction.Operation)
                    .Distinct(),
            ];

        Assert.Contains(CilOperation.LoadArgumentAddress, operations);
        Assert.Contains(CilOperation.LoadLocalAddress, operations);
        Assert.Contains(CilOperation.InitializeObject, operations);
        Assert.Contains(CilOperation.Box, operations);
        Assert.Contains(CilOperation.UnboxAny, operations);
        Assert.Contains(CilOperation.Constrained, operations);
        Assert.Contains(CilOperation.CallVirtual, operations);

    }

    [Fact]
    public void DecoderRetainsTypedExceptionRegionsAndNormalizedTransfers()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ExceptionMetadata",
            """
            namespace ExceptionMetadata;

            public static class EntryPoint
            {
                private static int _sink;

                public static int WithFinally(int input)
                {
                    try
                    {
                        return input + 1;
                    }
                    finally
                    {
                        _sink = input;
                    }
                }

                public static int WithCatch(int input)
                {
                    try
                    {
                        throw new System.Exception();
                    }
                    catch (System.Exception)
                    {
                        return input;
                    }
                }

                public static int WithFilter(int input)
                {
                    try
                    {
                        throw new System.Exception();
                    }
                    catch (System.Exception) when (input > 0)
                    {
                        return input;
                    }
                }
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var symbols = MetadataCapabilityTestData.Symbols(snapshot);
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var withFinally = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "ExceptionMetadata.EntryPoint",
            "WithFinally");
        var withCatch = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "ExceptionMetadata.EntryPoint",
            "WithCatch");
        var withFilter = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "ExceptionMetadata.EntryPoint",
            "WithFilter");

        var finallyBody = bodies.ReadMethodBody(withFinally);
        var finallyRegion = Assert.Single(finallyBody.ExceptionRegions);
        Assert.Equal(CilExceptionRegionKind.Finally, finallyRegion.Kind);
        Assert.Null(finallyRegion.CatchType);
        Assert.Null(finallyRegion.FilterOffset);
        Assert.Contains(finallyBody.Instructions, instruction =>
            instruction.Operation == CilOperation.Leave);
        Assert.Contains(finallyBody.Instructions, instruction =>
            instruction.Operation == CilOperation.EndFinally);

        var catchBody = bodies.ReadMethodBody(withCatch);
        var catchRegion = Assert.Single(catchBody.ExceptionRegions);
        Assert.Equal(CilExceptionRegionKind.Catch, catchRegion.Kind);
        Assert.NotNull(catchRegion.CatchType);
        Assert.Equal("System.Exception", symbols.Format(catchRegion.CatchType.Value));
        Assert.Contains(catchBody.Instructions, instruction =>
            instruction.Operation == CilOperation.Throw);
        Assert.Contains(catchBody.Instructions, instruction =>
            instruction.Operation == CilOperation.Pop);

        var filterBody = bodies.ReadMethodBody(withFilter);
        var filterRegion = Assert.Single(filterBody.ExceptionRegions);
        Assert.Equal(CilExceptionRegionKind.Filter, filterRegion.Kind);
        Assert.NotNull(filterRegion.FilterOffset);

    }

    [Fact]
    public void DecoderRetainsReferenceArrayConstructionAndAccess()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ArrayMetadata",
            """
            namespace ArrayMetadata;

            public sealed class Node
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new Node[2];
                    values[0] = new Node();
                    Node value = values[0];
                    return values.Length + (value == null ? 0 : input);
                }
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var typeFinder = MetadataCapabilityTestData.TypeFinder(snapshot);
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var run = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "ArrayMetadata.EntryPoint",
            "Run");

        var body = bodies.ReadMethodBody(run);

        Assert.Contains(body.Instructions, instruction =>
            instruction.Operation == CilOperation.NewArray &&
            instruction.Operand is CilOperand.TypeIdentity element &&
            element.Value.FullName == "ArrayMetadata.Node");
        Assert.Contains(body.Instructions, instruction =>
            instruction.Operation == CilOperation.StoreArrayElementReference);
        Assert.Contains(body.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadArrayElementReference);
        Assert.Contains(body.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadArrayLength);
        Assert.Equal("System.Array", typeFinder.FindType("System.Array").FullName);

    }

    [Fact]
    public void DecoderRetainsEveryPrimitiveArrayLoadAndStore()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PrimitiveArrays",
            """
            namespace PrimitiveArrays;

            public struct Pair
            {
                public int Value;
            }

            public static class EntryPoint
            {
                public static int SignedByte(sbyte[] values, sbyte value) { values[0] = value; return values[0]; }
                public static int Byte(byte[] values, byte value) { values[0] = value; return values[0]; }
                public static int Short(short[] values, short value) { values[0] = value; return values[0]; }
                public static int UShort(ushort[] values, ushort value) { values[0] = value; return values[0]; }
                public static int Int(int[] values, int value) { values[0] = value; return values[0]; }
                public static uint UInt(uint[] values, uint value) { values[0] = value; return values[0]; }
                public static long Long(long[] values, long value) { values[0] = value; return values[0]; }
                public static nint Native(nint[] values, nint value) { values[0] = value; return values[0]; }
                public static float Single(float[] values, float value) { values[0] = value; return values[0]; }
                public static double Double(double[] values, double value) { values[0] = value; return values[0]; }
                public static Pair Value(Pair[] values, Pair value) { values[0] = value; return values[0]; }
                public static long LongConstant() => 0x102030405060708L;
                public static float SingleConstant() => 1.25f;
                public static double DoubleConstant() => 2.5d;
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(assembly, [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var identities = new HashSet<string>();
        foreach (var name in PrimitiveArrayMethods)
        {
            var body = bodies.ReadMethodBody(methods.FindMethod(
                snapshot.EntryAssemblyIdentity,
                "PrimitiveArrays.EntryPoint",
                name));
            foreach (var instruction in body.Instructions.Where(instruction =>
                         instruction.Operation is CilOperation.LoadArrayElement or
                             CilOperation.StoreArrayElement))
            {
                identities.Add(Assert.IsType<CilOperand.TypeIdentity>(instruction.Operand)
                    .Value.CanonicalName);
            }
        }

        Assert.Contains("primitive:i1", identities);
        Assert.Contains("primitive:u1", identities);
        Assert.Contains("primitive:i2", identities);
        Assert.Contains("primitive:u2", identities);
        Assert.Contains("primitive:i4", identities);
        Assert.Contains("primitive:u4", identities);
        Assert.Contains("primitive:i8", identities);
        Assert.Contains("primitive:nativeint", identities);
        Assert.Contains("primitive:f4", identities);
        Assert.Contains("primitive:f8", identities);

    }

    [Fact]
    public void DecoderAcceptsEveryRecognizedOpcodeShapeWithoutCompilerIntegration()
    {
        using var assets = TestAssets.Create();
        using var original = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var bodyReader = new MetadataMethodBodyBlockReader(new FixedSymbols());
        var target = original.Methods.Values
            .Where(method => method.HasBody)
            .Select(method => (Method: method, Body: bodyReader.Read(
                original.PortableExecutableReader, method)))
            .Where(candidate => candidate.Body.GetILBytes() is not null &&
                candidate.Body.ExceptionRegions.IsEmpty)
            .OrderByDescending(candidate => candidate.Body.GetILBytes()!.Length)
            .First();
        var typeToken = original.Types.Keys.First();
        var fieldToken = original.Fields.Keys.First();
        const int stringToken = 0x70000001;
        const int signatureToken = 0x11000001;
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<OpCode>()
            .GroupBy(opCode => opCode.Value)
            .Select(group => group.First())
            .ToArray();
        var decoded = 0;

        foreach (var opCode in opCodes)
        {
            var operand = OperandBytes(opCode.OperandType, typeToken, fieldToken,
                stringToken, signatureToken);
            var encoded = Encode(opCode, operand);
            if (encoded.Length > target.Body.GetILBytes()!.Length)
            {
                continue;
            }

            var image = File.ReadAllBytes(assets.CoreLib);
            PatchMethodBody(image, original, target.Method, encoded);
            var patchedPath = Path.Combine(
                assets.Directory, $"opcode-{unchecked((ushort)opCode.Value):x4}.dll");
            File.WriteAllBytes(patchedPath, image);
            using var assembly = ManagedAssemblyTestFactory.Load(patchedPath);
            var method = assembly.Methods[target.Method.Key.MetadataToken];
            var decoder = new CilDecoder(
                new MetadataMethodBodyBlockReader(new FixedSymbols()),
                new FixedSymbols(),
                new FixedMethodReferences(method),
                new FixedTypeEntities(),
                new FixedFieldReferences(assembly.Fields.Values.First(), false),
                new FixedTypeSignatures(),
                new FixedCallSiteSignatures(),
                new IdentityMetadataStackTypeResolver(),
                new IdentitySwitchLowerer());
            try
            {
                _ = ((ICilDecoder)decoder).Decode(
                    assembly,
                    new MethodInstanceModel(
                        method,
                        CliTypeIdentity.Named(assembly.Identity, "Test", "Type", false),
                        [],
                        method.Signature));
                decoded++;
            }
            catch (CompilerException exception) when (
                exception.Diagnostic.Code == DiagnosticCode.UnsupportedCil)
            {
                // OpCodes contains metadata operations not represented by the decoder.
            }
        }

        Assert.True(decoded > 100);

        var invalidType = DecodePatched(
            Encode(OpCodes.Castclass, BitConverter.GetBytes(fieldToken)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, invalidType!.Diagnostic.Code);
        Assert.Null(DecodePatched(
            Encode(OpCodes.Ldtoken, BitConverter.GetBytes(fieldToken))));

        var validSwitch = DecodePatched(
            Encode(OpCodes.Switch, [1, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Null(validSwitch);

        var invalidSwitch = DecodePatched(
            Encode(OpCodes.Switch, [0xff, 0xff, 0xff, 0xff]));
        Assert.Equal(DiagnosticCode.InvalidCil, invalidSwitch!.Diagnostic.Code);

        var invalidOneByte = DecodePatched([0xf0]);
        Assert.Equal(DiagnosticCode.InvalidCil, invalidOneByte!.Diagnostic.Code);

        var invalidTwoByte = DecodePatched([0xfe, 0xff]);
        Assert.Equal(DiagnosticCode.InvalidCil, invalidTwoByte!.Diagnostic.Code);

        var truncatedOperand = DecodePatched(
            Encode(OpCodes.Ldarg_S, []), codeSizeOverride: 1);
        Assert.Equal(DiagnosticCode.InvalidCil, truncatedOperand!.Diagnostic.Code);

        Assert.Null(DecodePatched([], genericInstance: true));
        Assert.Null(DecodePatched(
            Encode(OpCodes.Call, BitConverter.GetBytes(fieldToken)),
            constructedMethod: true));
        Assert.Null(DecodePatched(
            Encode(OpCodes.Ldfld, BitConverter.GetBytes(fieldToken)),
            constructedField: true));

        CompilerException? DecodePatched(
            byte[] encoded,
            int? codeSizeOverride = null,
            bool genericInstance = false,
            bool constructedMethod = false,
            bool constructedField = false)
        {
            var image = File.ReadAllBytes(assets.CoreLib);
            PatchMethodBody(image, original, target.Method, encoded, codeSizeOverride);
            var patchedPath = Path.Combine(
                assets.Directory, "opcode-special.dll");
            File.WriteAllBytes(patchedPath, image);
            using var assembly = ManagedAssemblyTestFactory.Load(patchedPath);
            var method = assembly.Methods[target.Method.Key.MetadataToken];
            var decoder = new CilDecoder(
                new MetadataMethodBodyBlockReader(new FixedSymbols()),
                new FixedSymbols(),
                new FixedMethodReferences(method, constructedMethod),
                new FixedTypeEntities(),
                new FixedFieldReferences(assembly.Fields.Values.First(), constructedField),
                new FixedTypeSignatures(),
                new FixedCallSiteSignatures(),
                new IdentityMetadataStackTypeResolver(),
                new IdentitySwitchLowerer());
            try
            {
                _ = ((ICilDecoder)decoder).Decode(
                    assembly,
                    new MethodInstanceModel(
                        method,
                        genericInstance
                            ? CliTypeIdentity.GenericInstantiation(
                                CliTypeIdentity.Named(assembly.Identity, "Test", "Type", false),
                                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
                            : CliTypeIdentity.Named(assembly.Identity, "Test", "Type", false),
                        genericInstance
                            ? [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]
                            : [],
                        method.Signature));
                return null;
            }
            catch (CompilerException exception)
            {
                return exception;
            }
        }
    }

    private static byte[] OperandBytes(
        OperandType operandType,
        int typeToken,
        int fieldToken,
        int stringToken,
        int signatureToken) => operandType switch
        {
            OperandType.InlineNone => [],
            OperandType.ShortInlineI or OperandType.ShortInlineBrTarget or
                OperandType.ShortInlineVar => [0],
            OperandType.InlineI or OperandType.InlineBrTarget or
                OperandType.InlineField or OperandType.InlineMethod or
                OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.InlineType =>
                BitConverter.GetBytes(operandType switch
                {
                    OperandType.InlineField => fieldToken,
                    OperandType.InlineMethod => fieldToken,
                    OperandType.InlineSig => signatureToken,
                    OperandType.InlineString => stringToken,
                    _ => typeToken,
                }),
            OperandType.InlineI8 or OperandType.InlineR => new byte[sizeof(long)],
            OperandType.ShortInlineR => new byte[sizeof(float)],
            OperandType.InlineVar => new byte[sizeof(ushort)],
            OperandType.InlineSwitch => new byte[sizeof(int)],
            _ => throw new InvalidOperationException($"unsupported operand type {operandType}"),
        };

    private static byte[] Encode(OpCode opCode, byte[] operand)
    {
        var value = unchecked((ushort)opCode.Value);
        var prefix = value > byte.MaxValue
            ? new[] { (byte)0xfe, (byte)value }
            : new[] { (byte)value };
        return [.. prefix, .. operand];
    }

    private static void PatchMethodBody(
        byte[] image,
        ManagedAssembly source,
        MethodDefinitionModel method,
        byte[] encoded,
        int? codeSizeOverride = null)
    {
        var rva = method.RelativeVirtualAddress;
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var section = pe.PEHeaders.SectionHeaders.Single(candidate =>
        {
            var length = Math.Max(candidate.VirtualSize, candidate.SizeOfRawData);
            return rva >= candidate.VirtualAddress &&
                rva < candidate.VirtualAddress + length;
        });
        var fileOffset = section.PointerToRawData + rva - section.VirtualAddress;
        var first = image[fileOffset];
        var headerSize = (first & 3) == 2
            ? 1
            : ((BinaryPrimitives.ReadUInt16LittleEndian(
                image.AsSpan(fileOffset, sizeof(ushort))) >> 12) * sizeof(uint));
        var codeSize = (first & 3) == 2
            ? first >> 2
            : BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(fileOffset + sizeof(uint)));
        var codeOffset = fileOffset + headerSize;
        var patchedCodeSize = codeSizeOverride ?? codeSize;
        if (codeSizeOverride is int overrideSize)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(fileOffset + sizeof(uint)), overrideSize);
        }
        Array.Fill(image, (byte)0, codeOffset, codeSize);
        encoded.CopyTo(image, codeOffset);
    }

    private sealed class FixedSymbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => "Test.Type";
        public string Format(MethodDefinitionModel method) => "Test.Type::Run";
    }

    private sealed class FixedMethodReferences(
        MethodDefinitionModel method,
        bool constructed = false) :
        IMetadataMethodReferenceResolver
    {
        public MethodInstanceModel Resolve(
            MetadataAssemblySnapshot source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            new(
                method,
                constructed
                    ? CliTypeIdentity.GenericInstantiation(
                        CliTypeIdentity.Named(source.Identity, "Test", "Type", false),
                        [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
                    : CliTypeIdentity.Named(source.Identity, "Test", "Type", false),
                [],
                method.Signature);
    }

    private sealed class FixedTypeEntities : IMetadataTypeEntityResolver
    {
        public EntityKey Resolve(MetadataAssemblySnapshot source, EntityHandle handle) =>
            new(source.Identity, MetadataTokens.GetToken(handle));
    }

    private sealed class FixedFieldReferences(FieldDefinitionModel field, bool constructed) :
        IMetadataFieldReferenceResolver
    {
        public FieldInstanceModel Resolve(
            MetadataAssemblySnapshot source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            new(
                field,
                constructed
                    ? CliTypeIdentity.GenericInstantiation(
                        CliTypeIdentity.Named(source.Identity, "Test", "Type", false),
                        [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
                    : CliTypeIdentity.Named(source.Identity, "Test", "Type", false),
                field.SignatureType);
    }

    private sealed class FixedTypeSignatures : IMetadataTypeSignatureResolver
    {
        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            int metadataToken,
            CliGenericContext? genericContext = null) =>
            CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    }

    private sealed class FixedCallSiteSignatures : IMetadataCallSiteSignatureResolver
    {
        public MethodSignatureModel Resolve(
            MetadataAssemblySnapshot source,
            int metadataToken,
            CliGenericContext genericContext) =>
            MethodSignatureModel.Create(CliValueKind.Void);
    }

    private sealed class IdentityMetadataStackTypeResolver : IMetadataStackTypeResolver
    {
        public CliTypeIdentity Resolve(CliTypeIdentity type) => type;

        public CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext)
        {
            var context = genericContext.Normalize();
            return Resolve(type.Substitute(context.TypeArguments, context.MethodArguments));
        }

        public MethodSignatureModel Resolve(MethodSignatureModel signature) => signature;

        public MethodSignatureModel Resolve(
            MethodSignatureModel signature,
            CliGenericContext genericContext)
        {
            var context = genericContext.Normalize();
            return Resolve(signature.Substitute(context.TypeArguments, context.MethodArguments));
        }
    }

    private sealed class IdentitySwitchLowerer : ICilSwitchLowerer
    {
        public LoweredSwitchBody Lower(
            ImmutableArray<CilInstruction> instructions,
            ImmutableArray<CilExceptionRegion> exceptionRegions) =>
            new(instructions, exceptionRegions, false, 0);
    }

    [Fact]
    public void DecoderRetainsCheckedIntegerArithmeticAndClassCasts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CheckedAndCastMetadata",
            """
            namespace CheckedAndCastMetadata;

            public sealed class Target
            {
            }

            public static class EntryPoint
            {
                public static int Checked(int left, int right)
                {
                    int added = checked(left + right);
                    int subtracted = checked(added - right);
                    return checked(subtracted * right);
                }

                public static uint CheckedUnsigned(uint left, uint right)
                {
                    var added = checked(left + right);
                    var subtracted = checked(added - right);
                    return checked(subtracted * right);
                }

                public static Target Cast(object value)
                {
                    return (Target)value;
                }

                public static bool IsInt(object value) => value is int;
            }
            """);
        using var lease = MetadataCompilationTestFactory.Load(
            assembly,
            [assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var checkedBody = bodies.ReadMethodBody(methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "CheckedAndCastMetadata.EntryPoint",
            "Checked"));
        Assert.Contains(checkedBody.Instructions, instruction =>
            instruction.Operation == CilOperation.AddChecked);
        Assert.Contains(checkedBody.Instructions, instruction =>
            instruction.Operation == CilOperation.SubtractChecked);
        Assert.Contains(checkedBody.Instructions, instruction =>
            instruction.Operation == CilOperation.MultiplyChecked);
        var checkedUnsignedBody = bodies.ReadMethodBody(methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "CheckedAndCastMetadata.EntryPoint",
            "CheckedUnsigned"));
        Assert.Contains(checkedUnsignedBody.Instructions, instruction =>
            instruction.Operation == CilOperation.AddCheckedUnsigned);
        Assert.Contains(checkedUnsignedBody.Instructions, instruction =>
            instruction.Operation == CilOperation.SubtractCheckedUnsigned);
        Assert.Contains(checkedUnsignedBody.Instructions, instruction =>
            instruction.Operation == CilOperation.MultiplyCheckedUnsigned);

        var cast = Assert.Single(
            bodies.ReadMethodBody(methods.FindMethod(
                    snapshot.EntryAssemblyIdentity,
                    "CheckedAndCastMetadata.EntryPoint",
                    "Cast"))
                .Instructions,
            instruction => instruction.Operation == CilOperation.CastClass);
        var target = Assert.IsType<CilOperand.TypeIdentity>(cast.Operand).Value;
        Assert.Equal("CheckedAndCastMetadata.Target", target.FullName);

        var isInt = Assert.Single(
            bodies.ReadMethodBody(methods.FindMethod(
                    snapshot.EntryAssemblyIdentity,
                    "CheckedAndCastMetadata.EntryPoint",
                    "IsInt"))
                .Instructions,
            instruction => instruction.Operation == CilOperation.IsInstance);
        Assert.Equal(
            "primitive:i4",
            Assert.IsType<CilOperand.TypeIdentity>(isInt.Operand).Value.CanonicalName);

    }

}
