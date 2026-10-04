using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Catalogs;

public sealed class WitJavaScriptNameFormatterTests
{
    private readonly WitJavaScriptNameFormatter _subject = new();

    [Theory]
    [InlineData("URL-value", "urlValue")]
    [InlineData("HTTP-status", "httpStatus")]
    [InlineData("foo-BAR", "fooBar")]
    [InlineData("URL", "url")]
    [InlineData("url", "url")]
    [InlineData("HTTP2-3D-value", "http23dValue")]
    [InlineData("value", "value")]
    [InlineData("initialization-count", "initializationCount")]
    [InlineData("one-two-three", "oneTwoThree")]
    public void FormatsPinnedJcoMemberNames(string witName, string expected) =>
        Assert.Equal(expected, _subject.FormatMember(witName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingNames(string? witName) =>
        Assert.ThrowsAny<ArgumentException>(() => _subject.FormatMember(witName!));

    [Theory]
    [InlineData("-")]
    [InlineData("value-")]
    public void RejectsIncompleteNames(string witName)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            _subject.FormatMember(witName));
        Assert.Equal("NW1009", exception.Diagnostic.Id);
    }
}
