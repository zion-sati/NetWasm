using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

// What desktop .NET 11 does with each set of CallConv types (CoreCLR's CallConvBuilder).
public sealed class UnmanagedCallingConventionsTests
{
    private const string Namespace = "System.Runtime.CompilerServices.";

    [Theory]
    [InlineData]
    [InlineData("CallConvCdecl")]
    [InlineData("CallConvStdcall")]
    [InlineData("CallConvThiscall")]
    [InlineData("CallConvSuppressGCTransition")]
    [InlineData("CallConvMemberFunction")]
    [InlineData("CallConvStdcall", "CallConvMemberFunction", "CallConvSuppressGCTransition")]
    [InlineData("CallConvUnknown", "CallConvCdecl")]
    public void TheCAbiHonoursEachBaseConventionAndModifier(params string[] conventions) =>
        Assert.Null(UnmanagedCallingConventions.Unsupported(conventions.Select(static name => Namespace + name)));

    // A type without a name, or outside the CallConv namespace, names no convention.
    [Fact]
    public void IgnoresUnnamedAndUnknownTypes() =>
        Assert.Null(UnmanagedCallingConventions.Unsupported([null, "Fixture.CallConvFastcall", Namespace + "CallConvStdcall"]));

    [Theory]
    [InlineData("CallConvFastcall")]
    [InlineData("CallConvSwift")]
    public void FastcallAndSwiftAreNotSupported(string convention) =>
        Assert.Equal(
            $"calling convention '{Namespace}{convention}' is not supported",
            UnmanagedCallingConventions.Unsupported([Namespace + "CallConvSuppressGCTransition", Namespace + convention]));

    // CoreCLR fails a second base convention, even the same one again.
    [Theory]
    [InlineData("CallConvCdecl", "CallConvStdcall")]
    [InlineData("CallConvCdecl", "CallConvCdecl")]
    [InlineData("CallConvFastcall", "CallConvThiscall")]
    public void TwoBaseConventionsAreNotSupported(string first, string second) =>
        Assert.Equal(
            $"calling conventions '{Namespace}{first}' and '{Namespace}{second}' are both named; only one is supported",
            UnmanagedCallingConventions.Unsupported([Namespace + first, Namespace + "CallConvMemberFunction", Namespace + second]));
}
