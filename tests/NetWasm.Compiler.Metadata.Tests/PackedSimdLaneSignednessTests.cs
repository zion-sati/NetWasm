using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// Each of CoreLib's PackedSimd overloads lowers to an instruction of its
// lanes' signedness. An overload whose name does not choose signed or unsigned
// lanes takes its element type's; Wasm has no unsigned 64-bit lane comparison,
// so ulong compares with the sign bit flipped, as nuint does on wasm64, and no
// other overload flips it.
public sealed class PackedSimdLaneSignednessTests
{
    private static readonly string[] Explicit = ["Arithmetic", "Logical", "SignExtend", "ZeroExtend", "Unsigned", "Signed"];

    [Fact]
    public void EveryOverloadsInstructionTakesItsElementsSignedness()
    {
        using var assets = TestAssets.Create();
        using var coreLib = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var packedSimd = coreLib.Types.Values.Single(type => type.FullName == PackedSimdInstructions.DeclaringType);
        var overloads = packedSimd.Methods
            .Select(key => coreLib.Methods[key.MetadataToken])
            .Where(static method => method.IsPublic && method.Name != "get_IsSupported")
            .ToArray();
        var flipped = new List<string>();
        var wideFlipped = new List<string>();

        foreach (var method in overloads)
        {
            Assert.True(PackedSimdInstructions.TryGet(method, out var instruction), $"{method.Name} has no instruction");
            var parameters = method.Signature.ParameterSignatureTypes;
            // A load's memory elements, or the first vector's.
            var element = parameters.FirstOrDefault(static type => type.Shape == CliTypeShape.UnmanagedPointer)?.ElementType ??
                parameters.FirstOrDefault(static type => type.Shape == CliTypeShape.GenericInstantiation)?.TypeArguments[0];
            var signedness = element?.CanonicalName switch
            {
                "primitive:u1" or "primitive:u2" or "primitive:u4" or "primitive:u8" or "primitive:nativeuint" => 'u',
                "primitive:i1" or "primitive:i2" or "primitive:i4" or "primitive:i8" or "primitive:nativeint" => 's',
                _ => (char?)null,
            };
            var suffix = Suffix(instruction.Name);
            var description = $"{method.Name}({element?.CanonicalName}) -> {instruction.Name}";
            if (instruction.FlipsSign) flipped.Add(description);
            if (instruction.Wide.FlipsSign) wideFlipped.Add(description);
            if (suffix is null || signedness is null || Explicit.Any(word => method.Name.Contains(word, StringComparison.Ordinal)))
            {
                continue;
            }
            Assert.True(suffix == signedness || instruction.FlipsSign, $"{description} takes {signedness} lanes");
        }

        // Exactly the ordered comparisons of unsigned 64-bit lanes flip the sign.
        Assert.Equal(4, flipped.Count);
        Assert.All(flipped, description => Assert.Matches(@"^Compare\w+\(primitive:u8\) -> i64x2\.(lt|gt|le|ge)_s$", description));
        Assert.Equal(8, wideFlipped.Count);
        Assert.All(wideFlipped, description => Assert.Matches(
            @"^Compare\w+\(primitive:(u8|nativeuint)\) -> i(64x2|32x4)\.(lt|gt|le|ge)_[su]$", description));
        Assert.Equal(470, overloads.Length);
    }

    private static char? Suffix(string instruction)
    {
        var operation = instruction[(instruction.IndexOf('.') + 1)..];
        var match = System.Text.RegularExpressions.Regex.Match(operation, "_(s|u)(?:$|_)");
        return match.Success ? match.Groups[1].Value[0] : null;
    }
}
