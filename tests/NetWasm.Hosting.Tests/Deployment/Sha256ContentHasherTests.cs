using System.Text;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class Sha256ContentHasherTests
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("hello", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
    public void ProducesCanonicalSha256(string text, string expected)
    {
        var subject = Assert.IsAssignableFrom<IContentHasher>(new Sha256ContentHasher());
        Assert.Equal(expected, subject.Hash(Encoding.UTF8.GetBytes(text)));
    }
}
