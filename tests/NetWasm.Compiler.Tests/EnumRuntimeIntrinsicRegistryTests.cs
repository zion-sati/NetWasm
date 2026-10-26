using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class EnumRuntimeIntrinsicRegistryTests
{
    [Fact]
    public void RegistryMapsEveryEnumRuntimeContract()
    {
        var assembly = new AssemblyIdentity("EnumIntrinsicTests");
        var declaringType = new EntityKey(assembly, 1);
        var definitions = new[]
        {
            Method(2, "InternalEquals"),
            Method(3, "InternalGetHashCode"),
            Method(4, "InternalCompareTo"),
            Method(5, "InternalGetTypeCode"),
            Method(6, "InternalHasFlag"),
            Method(7, "InternalGetNames", genericArity: 1),
            Method(8, "InternalGetNames"),
            Method(9, "InternalGetName", genericArity: 1),
            Method(10, "InternalGetName"),
            Method(11, "InternalGetValues", genericArity: 1),
            Method(12, "InternalGetValues"),
            Method(13, "InternalGetValuesAsUnderlyingType", genericArity: 1),
            Method(14, "InternalGetValuesAsUnderlyingType"),
            Method(15, "InternalIsDefined", genericArity: 1),
            Method(16, "InternalIsDefined"),
            Method(17, "InternalGetMetadata", genericArity: 1),
            Method(18, "InternalGetMetadata"),
            Method(19, "InternalGetUnderlyingType"),
            Method(20, "InternalToString"),
            Method(21, "InternalFormat"),
            Method(22, "InternalToObject"),
            Method(23, "InternalToBoolean"),
            Method(24, "InternalToChar"),
            Method(25, "InternalToSByte"),
            Method(26, "InternalToByte"),
            Method(27, "InternalToInt16"),
            Method(28, "InternalToUInt16"),
            Method(29, "InternalToInt32"),
            Method(30, "InternalToUInt32"),
            Method(31, "InternalToInt64"),
            Method(32, "InternalToUInt64"),
            Method(33, "InternalToSingle"),
            Method(34, "InternalToDouble"),
            Method(35, "InternalToDecimal"),
            Method(36, "InternalToDateTime"),
            Method(38, "System.IConvertible.ToBoolean"),
            Method(39, "System.IConvertible.ToChar"),
            Method(40, "System.IConvertible.ToSByte"),
            Method(41, "System.IConvertible.ToByte"),
            Method(42, "System.IConvertible.ToInt16"),
            Method(43, "System.IConvertible.ToUInt16"),
            Method(44, "System.IConvertible.ToInt32"),
            Method(45, "System.IConvertible.ToUInt32"),
            Method(46, "System.IConvertible.ToInt64"),
            Method(47, "System.IConvertible.ToUInt64"),
            Method(48, "System.IConvertible.ToSingle"),
            Method(49, "System.IConvertible.ToDouble"),
        };
        var registry = new RuntimeIntrinsicRegistry(
            new EnumSymbolFormatter(declaringType),
            definitions);

        var expected = new Dictionary<string, RuntimeIntrinsic>
        {
            ["InternalEquals"] = RuntimeIntrinsic.EnumEquals,
            ["InternalGetHashCode"] = RuntimeIntrinsic.EnumGetHashCode,
            ["InternalCompareTo"] = RuntimeIntrinsic.EnumCompareTo,
            ["InternalGetTypeCode"] = RuntimeIntrinsic.EnumGetTypeCode,
            ["InternalHasFlag"] = RuntimeIntrinsic.EnumHasFlag,
            ["InternalGetNames"] = RuntimeIntrinsic.EnumGetNames,
            ["InternalGetName"] = RuntimeIntrinsic.EnumGetName,
            ["InternalGetValues"] = RuntimeIntrinsic.EnumGetValues,
            ["InternalGetValuesAsUnderlyingType"] = RuntimeIntrinsic.EnumGetValues,
            ["InternalIsDefined"] = RuntimeIntrinsic.EnumIsDefined,
            ["InternalGetMetadata"] = RuntimeIntrinsic.EnumGetMetadata,
            ["InternalGetUnderlyingType"] = RuntimeIntrinsic.EnumGetUnderlyingType,
            ["InternalToString"] = RuntimeIntrinsic.EnumToString,
            ["InternalFormat"] = RuntimeIntrinsic.EnumFormat,
            ["InternalToObject"] = RuntimeIntrinsic.EnumToObject,
            ["InternalToBoolean"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToChar"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToSByte"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToByte"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToInt16"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToUInt16"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToInt32"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToUInt32"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToInt64"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToUInt64"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToSingle"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToDouble"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToDecimal"] = RuntimeIntrinsic.EnumConvert,
            ["InternalToDateTime"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToBoolean"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToChar"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToSByte"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToByte"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToInt16"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToUInt16"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToInt32"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToUInt32"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToInt64"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToUInt64"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToSingle"] = RuntimeIntrinsic.EnumConvert,
            ["System.IConvertible.ToDouble"] = RuntimeIntrinsic.EnumConvert,
        };

        foreach (var definition in definitions)
        {
            Assert.True(registry.TryGetIntrinsic(definition.Key, out var actual));
            Assert.Equal(expected[definition.Name], actual);
        }
    }

    [Fact]
    public void EnumToTypeUsesItsManagedBody()
    {
        var method = Method(52, "System.IConvertible.ToType");
        var registry = new RuntimeIntrinsicRegistry(
            new EnumSymbolFormatter(method.DeclaringType), [method]);
        Assert.False(registry.TryGetIntrinsic(method.Key, out _));
    }

    [Fact]
    public void RegistryIgnoresMethodsOutsideSystemEnum()
    {
        var assembly = new AssemblyIdentity("EnumIntrinsicTests");
        var enumType = new EntityKey(assembly, 1);
        var otherType = new EntityKey(assembly, 3);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            otherType,
            "InternalToString",
            true,
            MethodSignatureModel.Create(CliValueKind.ManagedReference),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new EnumSymbolFormatter(enumType),
            [method]);

        Assert.False(registry.TryGetIntrinsic(method.Key, out _));
    }

    private static MethodDefinitionModel Method(
        int token,
        string name,
        int genericArity = 0) => new(
        new EntityKey(new AssemblyIdentity("EnumIntrinsicTests"), token),
        new EntityKey(new AssemblyIdentity("EnumIntrinsicTests"), 1),
        name,
        true,
        MethodSignatureModel.Create(CliValueKind.ManagedReference),
        0)
        {
            GenericArity = genericArity,
        };

    private sealed class EnumSymbolFormatter(EntityKey enumType) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == enumType ? "System.Enum" : "Other.Type";

        public string Format(MethodDefinitionModel method) =>
            Format(method.DeclaringType) + "::" + method.Name;
    }
}
